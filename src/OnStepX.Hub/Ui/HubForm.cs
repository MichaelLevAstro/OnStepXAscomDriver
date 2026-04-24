using System;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Hardware;
using ASCOM.OnStepX.Hardware.State;
using ASCOM.OnStepX.Hardware.Transport;
using ASCOM.OnStepX.Ui.Theming;

namespace ASCOM.OnStepX.Ui
{
    public sealed class HubForm : Form
    {
        private enum ConnState { Disconnected, Connecting, Connected }
        private ConnState _connState = ConnState.Disconnected;
        private readonly System.Collections.Generic.List<Control> _mountActionControls = new System.Collections.Generic.List<Control>();
        // Controls usable when either disconnected or connected, but locked out
        // during the transient Connecting state (prevents the user triggering
        // wire-side operations while the transport is being brought up).
        private readonly System.Collections.Generic.List<Control> _disableWhileConnectingControls = new System.Collections.Generic.List<Control>();
        private readonly System.Collections.Generic.List<Control> _connectionControls = new System.Collections.Generic.List<Control>();

        private ComboBox _transportKind, _portCombo;
        private TextBox _hostBox;
        private NumericUpDown _baudBox, _tcpPortBox;
        private FlatButton _autoDetectBtn, _connectBtn, _disconnectBtn;
        private StatusLabel _statusLed;
        private StatusLabel _stateLabel;
        private SlewingBadge _slewingBadge;
        private StatusLabel _clientsStatus;

        private RichTextBox _ioBox;
        private ThemedCheckBox _ioEnable;
        private TextBox _ioFilter;
        // Console entry kinds drive the rendered color. Pair = normal cmd/reply
        // (green), Invalid = pair whose reply is empty/nan/0 (orange, flags failed
        // sets and missing data), Note = TransportLogger.Note diagnostics (white).
        private struct ConsoleEntry { public string Text; public Color Color; }
        private readonly System.Collections.Concurrent.ConcurrentQueue<ConsoleEntry> _ioPending = new System.Collections.Concurrent.ConcurrentQueue<ConsoleEntry>();
        // Full history (pre-filter); rendered through _ioFilter into _ioBox.
        private readonly System.Collections.Generic.LinkedList<ConsoleEntry> _ioAll = new System.Collections.Generic.LinkedList<ConsoleEntry>();
        private const int IoHistoryMax = 2000;
        // Colors resolved at emit time from the theme palette (ColResp/Danger/ColMeta)
        // so a theme switch immediately colors subsequent lines. Old constants kept
        // as name-tags in the call sites and resolved below.
        private static Color ConsoleNormalColor  => Theme.P.ColResp;
        private static Color ConsoleInvalidColor => Theme.P.Danger;
        private static Color ConsoleNoteColor    => Theme.P.ColMeta;

        private Panel _logPanel;
        private ThemedCheckBox _consoleToggle;
        private TextBox _cmdInput;
        private FlatButton _cmdSendBtn;
        private const int ConsoleExpandedHeight = 280;
        private const int ConsoleCollapsedHeight = 40;
        private ThemedCheckBox _ioAutoScroll;

        private TextBox _latBox, _lonBox, _eleBox;
        private FlatButton _siteWriteBtn, _siteSyncPcBtn, _sitesBtn;

        private DateTimePicker _datePicker, _timePicker;
        private NumericUpDown _utcOffsetBox;
        private FlatButton _syncFromPcBtn, _setDateTimeBtn;

        private ThemedCheckBox _trackingCheck;
        private bool _suppressTrackingCheckEvent;
        private ComboBox _trackingModeBox;
        private bool _suppressTrackingModeEvent;
        private DateTime _trackingModeSetAt = DateTime.MinValue;
        private NumericUpDown _guideRateBox, _slewSpeedBox;
        private TrackBar _slewSpeedSlider;
        private bool _suppressSlewSyncEvent;
        private ComboBox _meridianActionBox;
        private FlatButton _advancedBtn;

        private NumericUpDown _horizonLimitBox, _overheadLimitBox;
        private NumericUpDown _meridianEastBox, _meridianWestBox;
        private NumericUpDown _syncLimitBox;
        private FlatButton _limitsWriteBtn;

        private Label _raLabel, _decLabel, _altLabel, _azLabel, _pierLabel, _lstLabel;

        private ThemedCheckBox _autoConnectCheck;
        private ThemedCheckBox _autoSyncTimeCheck;

        private FlatButton _parkBtn, _unparkBtn, _findHomeBtn, _goHomeBtn, _resetHomeBtn;
        private FlatButton _slewTargetBtn;

        private SlewPadControl _slewPad;

        private readonly Timer _uiTimer = new Timer { Interval = 250 };
        private readonly MountSession _mount = MountSession.Instance;
        private bool _hubConnected;
        private System.Threading.CancellationTokenSource _connectCts;

        public HubForm()
        {
            Text = "OnStepX ASCOM Driver \u00B7 " + GetVersionString();
            MinimumSize = new Size(1000, 900);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = AppIcons.App;
            BackColor = Theme.P.Bg;
            Font = new Font("Segoe UI", 8.75f);
            ForeColor = Theme.P.Text;
            Theme.Changed += (s, e) => ApplyFormTheme();
            BuildUi();
            LoadFromSettings();
            ApplyFormTheme();

            _uiTimer.Tick += (s, e) => { RefreshStatus(); DrainIo(); TickLocalTime(); };
            _uiTimer.Start();

            ClientRegistry.Changed += OnClientRegistryChanged;
            _mount.ConnectionChanged += OnMountConnectionChanged;
            TransportLogger.Pair += OnTransportPair;
            TransportLogger.Line += OnTransportLine;
            FormClosed += (s, e) => {
                TransportLogger.Pair -= OnTransportPair;
                TransportLogger.Line -= OnTransportLine;
            };
            UpdateClientLabel();

            // Reflect the initial Disconnected state on every action control so the UI
            // can't be poked before the mount is up. Auto-connect (if enabled) will
            // transition through Connecting -> Connected once Shown fires.
            ApplyConnState(ConnState.Disconnected);

            Shown += (s, e) => TryAutoConnect();
        }

        private void OnTransportPair(string cmd, string reply, int elapsedMs)
        {
            if (_ioEnable != null && !_ioEnable.Checked) return;
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " +
                          cmd.PadRight(14) + "  ->  " + reply +
                          (elapsedMs > 0 ? "  (" + elapsedMs + " ms)" : "");
            var color = IsReplyValid(cmd, reply) ? ConsoleNormalColor : ConsoleInvalidColor;
            EnqueueConsole(line, color);
        }

        // TransportLogger emits Tx/Rx (-> / <-) and Note (--) lines. The Pair event
        // already carries the cmd+reply pair in a single line, so routing Tx/Rx here
        // would just duplicate. Only Notes need surfacing.
        private void OnTransportLine(string rawLine)
        {
            if (_ioEnable != null && !_ioEnable.Checked) return;
            if (rawLine == null) return;
            if (!rawLine.StartsWith("-- ")) return;
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + rawLine;
            EnqueueConsole(line, ConsoleNoteColor);
        }

        private void EnqueueConsole(string line, Color color)
        {
            _ioPending.Enqueue(new ConsoleEntry { Text = line, Color = color });
            while (_ioPending.Count > 2000 && _ioPending.TryDequeue(out _)) { }
        }

        // Per-command reply validation. Generic "reply == 0 is bad" is wrong —
        // e.g. :GT# (tracking rate) can legitimately return "0.0#", and :MS#
        // returns "0" *on success*. Classify the command and apply the right
        // rule. Commands outside the known set stay uncolored (green), so the
        // console doesn't flag third-party / user-typed probes by mistake.
        private enum ReplyKind { Unknown, Ack01, SlewAck, Numeric, NonEmpty }

        private static ReplyKind ClassifyReply(string cmdCore)
        {
            if (string.IsNullOrEmpty(cmdCore)) return ReplyKind.Unknown;
            // Any :Sxxxx# set command → "1" on success.
            if (cmdCore[0] == 'S') return ReplyKind.Ack01;
            // Track on/off (blind TQ/TS/TL/TK don't reach here — they're "(blind)").
            if (cmdCore == "Te" || cmdCore == "Td") return ReplyKind.Ack01;
            // Park / unpark / set-park-here.
            if (cmdCore == "hP" || cmdCore == "hR" || cmdCore == "hQ") return ReplyKind.Ack01;
            // Slew: returns "0" on success, non-zero on error.
            if (cmdCore == "MS" || cmdCore == "MA") return ReplyKind.SlewAck;
            // Numeric getters (coordinates, times, slew rates, temperature, etc).
            switch (cmdCore)
            {
                case "GT": case "GS": case "GR": case "GRH": case "GD": case "GDH":
                case "GA": case "GZ": case "GL": case "Gt": case "GtH":
                case "Gg": case "GgH": case "GG": case "GC": case "Gv": case "Gh":
                    return ReplyKind.Numeric;
            }
            if (cmdCore.StartsWith("GX9", StringComparison.Ordinal)) return ReplyKind.Numeric;
            if (cmdCore.StartsWith("GXE", StringComparison.Ordinal)) return ReplyKind.Numeric;
            // String getters (versions, status, pier, home offsets, etc).
            switch (cmdCore)
            {
                case "GVP": case "GVN": case "GVM": case "GVD":
                case "GU": case "Gm": case "GW": case "GE": case "Go":
                    return ReplyKind.NonEmpty;
            }
            return ReplyKind.Unknown;
        }

        private static string CmdCore(string cmd)
        {
            var c = cmd ?? "";
            if (c.StartsWith(":")) c = c.Substring(1);
            if (c.EndsWith("#"))   c = c.Substring(0, c.Length - 1);
            int comma = c.IndexOf(',');
            if (comma >= 0) c = c.Substring(0, comma);
            return c;
        }

        private static bool IsReplyValid(string cmd, string reply)
        {
            if (reply == "(blind)") return true;
            var r = (reply ?? "").Trim().TrimEnd('#').Trim();
            var kind = ClassifyReply(CmdCore(cmd));
            switch (kind)
            {
                case ReplyKind.Ack01:    return r == "1";
                case ReplyKind.SlewAck:  return r.Length > 0 && r[0] == '0';
                case ReplyKind.Numeric:
                    if (r.Length == 0) return false;
                    if (r.Equals("nan", StringComparison.OrdinalIgnoreCase)) return false;
                    foreach (var ch in r) if (ch >= '0' && ch <= '9') return true;
                    return false;
                case ReplyKind.NonEmpty: return r.Length > 0;
                default: return true;
            }
        }

        private void DrainIo()
        {
            if (_ioBox == null) return;
            if (_ioPending.IsEmpty) return;
            string filter = _ioFilter?.Text ?? "";
            var toAppend = new System.Collections.Generic.List<ConsoleEntry>();
            while (_ioPending.TryDequeue(out var entry))
            {
                _ioAll.AddLast(entry);
                if (_ioAll.Count > IoHistoryMax) _ioAll.RemoveFirst();
                if (LineMatchesFilter(entry.Text, filter)) toAppend.Add(entry);
            }
            if (toAppend.Count == 0) return;
            const int maxChars = 200_000;
            int incoming = 0;
            foreach (var e in toAppend) incoming += e.Text.Length + 1;
            if (_ioBox.TextLength + incoming > maxChars)
            {
                int keep = Math.Max(0, maxChars - incoming);
                if (_ioBox.TextLength > keep) RebuildIoView();
            }
            bool autoScroll = _ioAutoScroll?.Checked ?? true;
            // Snapshot the user's scroll position before appending. AppendText
            // of a RichTextBox moves the caret to the end, which auto-scrolls.
            // When auto-scroll is off we restore the pixel-exact scroll offset
            // via EM_SETSCROLLPOS so the view doesn't jump as new lines land.
            POINT savedScroll = default;
            if (!autoScroll && _ioBox.IsHandleCreated)
                SendMessage(_ioBox.Handle, EM_GETSCROLLPOS, IntPtr.Zero, ref savedScroll);
            foreach (var e in toAppend) AppendColoredLine(_ioBox, e.Text, e.Color);
            if (autoScroll)
            {
                _ioBox.SelectionStart = _ioBox.TextLength;
                _ioBox.ScrollToCaret();
            }
            else if (_ioBox.IsHandleCreated)
            {
                SendMessage(_ioBox.Handle, EM_SETSCROLLPOS, IntPtr.Zero, ref savedScroll);
            }
        }

        // Filter: case-sensitive substring match over whitespace-separated tokens.
        // All tokens must be found literally (Ordinal) in the line.
        private static bool LineMatchesFilter(string line, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            foreach (var tok in filter.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (line.IndexOf(tok, StringComparison.Ordinal) < 0) return false;
            return true;
        }

        private void RebuildIoView()
        {
            if (_ioBox == null) return;
            string filter = _ioFilter?.Text ?? "";
            _ioBox.SuspendLayout();
            try
            {
                _ioBox.Clear();
                foreach (var e in _ioAll)
                    if (LineMatchesFilter(e.Text, filter)) AppendColoredLine(_ioBox, e.Text, e.Color);
            }
            finally { _ioBox.ResumeLayout(); }
            _ioBox.SelectionStart = _ioBox.TextLength;
            _ioBox.ScrollToCaret();
        }

        private static void AppendColoredLine(RichTextBox rtb, string line, Color fallback)
        {
            var p = Theme.P;
            // Expected shape: "HH:mm:ss.fff  CMD..  ->  REPLY  (Nms)" (pair) or
            // "HH:mm:ss.fff  -- note text" (note/meta). Anything else falls back to 'fallback'.
            // Segment coloring:
            //   ts  -> ColTs   cmd -> ColCmd   -> arrow -> TextFaint
            //   reply -> fallback (valid=ColResp, invalid=Danger, note=ColMeta)
            //   elapsed (Nms) -> ColMeta
            int first = line.IndexOf("  ", StringComparison.Ordinal);
            if (first < 13) { EmitSingle(rtb, line, fallback); return; }
            string ts = line.Substring(0, first);
            string rest = line.Substring(first + 2);
            // Note?
            if (rest.StartsWith("-- ", StringComparison.Ordinal))
            {
                EmitSeg(rtb, ts + "  ", p.ColTs);
                EmitSeg(rtb, rest, p.ColMeta);
                rtb.AppendText(Environment.NewLine);
                return;
            }
            int arrow = rest.IndexOf("  ->  ", StringComparison.Ordinal);
            if (arrow < 0) { EmitSingle(rtb, line, fallback); return; }
            string cmd = rest.Substring(0, arrow).TrimEnd();
            string cmdPad = rest.Substring(0, arrow);
            string after = rest.Substring(arrow + 6);
            // Split trailing "  (Nms)" if present.
            string reply = after;
            string elapsed = "";
            int paren = after.LastIndexOf("  (", StringComparison.Ordinal);
            if (paren >= 0 && after.EndsWith(")", StringComparison.Ordinal))
            {
                reply = after.Substring(0, paren);
                elapsed = after.Substring(paren);
            }
            EmitSeg(rtb, ts + "  ", p.ColTs);
            EmitSeg(rtb, cmdPad, p.ColCmd);
            EmitSeg(rtb, "  ->  ", p.TextFaint);
            EmitSeg(rtb, reply, fallback == p.ColResp ? p.ColResp : fallback);
            if (elapsed.Length > 0) EmitSeg(rtb, elapsed, p.ColMeta);
            rtb.AppendText(Environment.NewLine);
        }

        private static void EmitSeg(RichTextBox rtb, string s, Color color)
        {
            if (string.IsNullOrEmpty(s)) return;
            rtb.SelectionStart = rtb.TextLength;
            rtb.SelectionLength = 0;
            rtb.SelectionColor = color;
            rtb.AppendText(s);
        }

        private static void EmitSingle(RichTextBox rtb, string line, Color color)
        {
            rtb.SelectionStart = rtb.TextLength;
            rtb.SelectionLength = 0;
            rtb.SelectionColor = color;
            rtb.AppendText(line + Environment.NewLine);
        }

        // P/Invoke for precise scroll preservation when auto-scroll is off.
        // RichTextBox has no managed API to read or set the scroll offset as
        // a pixel/line coordinate — ScrollToCaret only centers on a caret.
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref POINT lParam);
        private const int EM_GETSCROLLPOS = 0x04DD;
        private const int EM_SETSCROLLPOS = 0x04DE;

        private void OnMountConnectionChanged(object sender, EventArgs e)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(new Action(SyncConnectionStateFromMount));
            }
            catch { }
        }

        private void SyncConnectionStateFromMount()
        {
            bool open = _mount.IsOpen;
            if (_connState == ConnState.Connecting)
            {
                // Stage 1 (transport-only up) leaves IsOpen=false — hold Connecting.
                // Stage 2 (mount responsive) flips IsOpen=true — promote to Connected.
                if (open && !_hubConnected)
                {
                    _hubConnected = true;
                    ApplyConnState(ConnState.Connected);
                }
                return;
            }
            if (open == _hubConnected) return;
            _hubConnected = open;
            ApplyConnState(open ? ConnState.Connected : ConnState.Disconnected);
        }

        private void OnClientRegistryChanged(object sender, EventArgs e)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(new Action(UpdateClientLabel));
            }
            catch { /* handle may be gone during shutdown */ }
        }

        // Invoked from two places: (1) the hub's own tray menu Show item, and
        // (2) HubPipeServer when a driver sends IPC:SHOWHUB on client Connect.
        // When started with --tray, Program.cs sets ShowInTaskbar=false; we must
        // flip it back to true here or the window pops up but the user has no
        // taskbar entry to Alt-Tab back to.
        public void EnsureVisibleFromClient()
        {
            try
            {
                if (IsDisposed) return;
                if (!IsHandleCreated) { var _ = Handle; } // force handle creation on UI thread owner
                if (InvokeRequired) { BeginInvoke(new Action(EnsureVisibleFromClient)); return; }
                if (!ShowInTaskbar) ShowInTaskbar = true;
                if (!Visible) Show();
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                BringToFront();
                Activate();
                if (!_hubConnected && _mount.IsOpen)
                {
                    // Client already opened the shared transport; reflect that in the UI.
                    _hubConnected = true;
                    ApplyConnState(ConnState.Connected);
                }
            }
            catch { }
        }

        private void TryAutoConnect()
        {
            if (_hubConnected || _mount.IsOpen) return;
            if (!DriverSettings.AutoConnect) return;
            if (!HasUsablePortSetting()) return;
            DoConnect();
        }

        private bool HasUsablePortSetting()
        {
            string kind = (DriverSettings.TransportKind ?? "").Trim();
            if (kind.Equals("TCP", StringComparison.OrdinalIgnoreCase))
                return !string.IsNullOrWhiteSpace(DriverSettings.TcpHost) && DriverSettings.TcpPort > 0;
            // Serial: require both a non-empty port name and that it actually exists right now.
            string port = (DriverSettings.SerialPort ?? "").Trim();
            if (string.IsNullOrEmpty(port)) return false;
            foreach (var p in System.IO.Ports.SerialPort.GetPortNames())
                if (string.Equals(p, port, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ---------- UI layout ----------
        private void BuildUi()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(8) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            Controls.Add(root);
            Controls.Add(BuildLogPanel());
            Controls.Add(BuildAppHeader());

            // NoAutoScrollFlowPanel suppresses ScrollControlIntoView so clicking
            // a button or focusing a nested control doesn't snap the panel's
            // scroll position. Manual scrollbar + wheel still work.
            var left = new NoAutoScrollFlowPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.P.Bg };
            var right = new NoAutoScrollFlowPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.P.Bg };
            ASCOM.OnStepX.Ui.Theming.DarkScroll.Apply(left);
            ASCOM.OnStepX.Ui.Theming.DarkScroll.Apply(right);
            Theme.Changed += (s, e) => { left.BackColor = Theme.P.Bg; right.BackColor = Theme.P.Bg; root.BackColor = Theme.P.Bg; };
            root.BackColor = Theme.P.Bg;
            root.Controls.Add(left, 0, 0);
            root.Controls.Add(right, 1, 0);

            left.Controls.Add(BuildConnectionGroup());
            left.Controls.Add(BuildSiteGroup());
            left.Controls.Add(BuildTimeGroup());
            left.Controls.Add(BuildTrackingGroup());
            left.Controls.Add(BuildLimitsGroup());

            right.Controls.Add(BuildPositionGroup());
            right.Controls.Add(BuildParkHomeGroup());
            right.Controls.Add(BuildSlewPadGroup());
            right.Controls.Add(BuildFooter());
        }

        private SectionPanel BuildConnectionGroup()
        {
            var g = NewGroup("Connection", 440, 190);
            _transportKind = new ComboBox { Left = 110, Top = 24, Width = 80, DropDownStyle = ComboBoxStyle.DropDownList };
            _transportKind.Items.AddRange(new object[] { "Serial", "TCP" });
            _portCombo = new ComboBox { Left = 110, Top = 58, Width = 100 };
            _portCombo.Items.AddRange(System.IO.Ports.SerialPort.GetPortNames());
            _baudBox = new NumericUpDown { Left = 260, Top = 58, Width = 90, Minimum = 1200, Maximum = 460800, Increment = 1200 };
            _hostBox = new TextBox { Left = 110, Top = 92, Width = 170 };
            _tcpPortBox = new NumericUpDown { Left = 290, Top = 92, Width = 60, Minimum = 1, Maximum = 65535 };
            _autoDetectBtn = new FlatButton { Text = "Auto-Detect", Left = 110, Top = 122, Width = 110 };
            _connectBtn = new FlatButton { Text = "Connect", Left = 228, Top = 122, Width = 90 };
            _disconnectBtn = new FlatButton { Text = "Disconnect", Left = 326, Top = 122, Width = 100 };
            ((FlatButton)_disconnectBtn).Kind = FlatButton.Variant.Primary;
            _statusLed = new StatusLabel { Left = 10, Top = 122, Width = 100, Height = 24 };
            _statusLed.Kind = PulseDot.StatusKind.Err;
            _statusLed.Text = "Disconnected";
            _autoConnectCheck = new ThemedCheckBox {
                Text = "Auto-connect to saved port on open", Left = 10, Top = 156, Width = 320,
                Checked = DriverSettings.AutoConnect
            };
            _autoConnectCheck.CheckedChanged += (s, e) => DriverSettings.AutoConnect = _autoConnectCheck.Checked;

            _autoDetectBtn.Click += (s, e) => DoAutoDetect();
            _connectBtn.Click += (s, e) => DoConnect();
            _disconnectBtn.Click += (s, e) => DoDisconnect();

            g.Controls.Add(new Label { Text = "Transport:", Left = 10, Top = 28, Width = 100 });
            g.Controls.Add(_transportKind);
            g.Controls.Add(new Label { Text = "COM port:", Left = 10, Top = 62, Width = 100 });
            g.Controls.Add(_portCombo);
            g.Controls.Add(new Label { Text = "Baud:", Left = 220, Top = 62, Width = 40 });
            g.Controls.Add(_baudBox);
            g.Controls.Add(new Label { Text = "TCP host/port:", Left = 10, Top = 96, Width = 100 });
            g.Controls.Add(_hostBox);
            g.Controls.Add(_tcpPortBox);
            g.Controls.Add(_autoDetectBtn);
            g.Controls.Add(_connectBtn);
            g.Controls.Add(_disconnectBtn);
            g.Controls.Add(_statusLed);
            g.Controls.Add(_autoConnectCheck);

            _connectionControls.Add(_transportKind);
            _connectionControls.Add(_portCombo);
            _connectionControls.Add(_baudBox);
            _connectionControls.Add(_hostBox);
            _connectionControls.Add(_tcpPortBox);
            _connectionControls.Add(_autoDetectBtn);
            _connectionControls.Add(_autoConnectCheck);
            return g;
        }

        private SectionPanel BuildSiteGroup()
        {
            var g = NewGroup("Site", 440, 160);
            _latBox = new TextBox { Left = 110, Top = 26, Width = 220 };
            _lonBox = new TextBox { Left = 110, Top = 54, Width = 220 };
            _eleBox = new TextBox { Left = 110, Top = 82, Width = 120 };
            _siteSyncPcBtn = new FlatButton { Text = "Sync from PC location", Left = 10, Top = 118, Width = 160 };
            _siteWriteBtn  = new FlatButton { Text = "Upload",               Left = 180, Top = 118, Width = 160 };
            _sitesBtn      = new FlatButton { Text = "Sites...",             Left = 350, Top = 118, Width = 80  };
            _siteSyncPcBtn.Click += (s, e) => DoSyncLocationFromPc();
            _siteWriteBtn.Click  += (s, e) => DoWriteSite();
            _sitesBtn.Click      += (s, e) => OpenSitesManager();
            g.Controls.Add(new Label { Text = "Latitude:", Left = 10, Top = 30, Width = 100 });
            g.Controls.Add(_latBox);
            g.Controls.Add(new Label { Text = "Longitude:", Left = 10, Top = 58, Width = 100 });
            g.Controls.Add(_lonBox);
            g.Controls.Add(new Label { Text = "Elevation (m):", Left = 10, Top = 86, Width = 100 });
            g.Controls.Add(_eleBox);
            g.Controls.Add(_siteSyncPcBtn);
            g.Controls.Add(_siteWriteBtn);
            g.Controls.Add(_sitesBtn);
            _mountActionControls.Add(_latBox);
            _mountActionControls.Add(_lonBox);
            _mountActionControls.Add(_eleBox);
            _mountActionControls.Add(_siteSyncPcBtn);
            _mountActionControls.Add(_siteWriteBtn);
            // _sitesBtn deliberately NOT in _mountActionControls — the sites
            // dialog edits a PC-local list and stays usable offline. Apply
            // button inside the dialog handles its own connection gating.
            // Still locked during Connecting to avoid wire contention if the
            // user clicks Apply mid-handshake.
            _disableWhileConnectingControls.Add(_sitesBtn);
            return g;
        }

        private void OpenSitesManager()
        {
            using (var dlg = new SitesManagerForm(_mount, _hubConnected))
            {
                var result = dlg.ShowDialog(this);
                if (result == DialogResult.OK && dlg.AppliedSite != null)
                {
                    // Reflect applied values into the Site group text boxes so
                    // the user sees what was just written to the mount.
                    _latBox.Text = CoordFormat.FormatLatitudeDms(dlg.AppliedSite.Latitude);
                    _lonBox.Text = CoordFormat.FormatLongitudeDms(dlg.AppliedSite.Longitude);
                    _eleBox.Text = dlg.AppliedSite.Elevation.ToString("F1", CultureInfo.InvariantCulture);
                }
            }
        }

        private SectionPanel BuildTimeGroup()
        {
            var g = NewGroup("Date / Time", 440, 168);
            _datePicker = new DateTimePicker { Left = 110, Top = 24, Width = 120, Format = DateTimePickerFormat.Short };
            _timePicker = new DateTimePicker { Left = 240, Top = 24, Width = 110, Format = DateTimePickerFormat.Time, ShowUpDown = true };
            // Timezone offset — east-positive civil value (Israel = +3). The driver
            // flips sign at the wire because OnStepX :SG uses the Meade west-positive
            // convention. UI stays in the intuitive civil sign so users don't have to
            // second-guess.
            _utcOffsetBox = new NumericUpDown { Left = 110, Top = 54, Width = 80, Minimum = -14, Maximum = 14, DecimalPlaces = 1, Increment = 0.5M };
            _syncFromPcBtn = new FlatButton { Text = "Sync from PC", Left = 210, Top = 52, Width = 140 };
            _syncFromPcBtn.Click += (s, e) => DoSyncTime();
            _setDateTimeBtn = new FlatButton { Text = "Upload", Left = 110, Top = 84, Width = 240 };
            _setDateTimeBtn.Click += (s, e) => DoWriteDateTime();
            _autoSyncTimeCheck = new ThemedCheckBox {
                Text = "Auto-sync date/time from PC on connect", Left = 10, Top = 116, Width = 340,
                Checked = DriverSettings.AutoSyncTimeOnConnect
            };
            _autoSyncTimeCheck.CheckedChanged += (s, e) => DriverSettings.AutoSyncTimeOnConnect = _autoSyncTimeCheck.Checked;
            g.Controls.Add(new Label { Text = "Local:", Left = 10, Top = 28, Width = 100 });
            g.Controls.Add(_datePicker);
            g.Controls.Add(_timePicker);
            g.Controls.Add(new Label { Text = "Timezone (h):", Left = 10, Top = 58, Width = 100 });
            g.Controls.Add(_utcOffsetBox);
            g.Controls.Add(_syncFromPcBtn);
            g.Controls.Add(_setDateTimeBtn);
            g.Controls.Add(_autoSyncTimeCheck);
            _mountActionControls.Add(_datePicker);
            _mountActionControls.Add(_timePicker);
            _mountActionControls.Add(_utcOffsetBox);
            _mountActionControls.Add(_syncFromPcBtn);
            _mountActionControls.Add(_setDateTimeBtn);
            _mountActionControls.Add(_autoSyncTimeCheck);
            return g;
        }

        private SectionPanel BuildTrackingGroup()
        {
            var g = NewGroup("Tracking / Slew", 440, 175);
            _trackingCheck = new ThemedCheckBox { Text = "Tracking enabled", Left = 10, Top = 26, Width = 160 };
            _trackingCheck.CheckedChanged += (s, e) =>
            {
                if (_suppressTrackingCheckEvent || !_hubConnected) return;
                try
                {
                    bool ok = _trackingCheck.Checked ? _mount.Protocol.TrackingOn() : _mount.Protocol.TrackingOff();
                    if (!ok)
                    {
                        string err = "";
                        try { err = _mount.Protocol.GetLastError(); } catch { }
                        string hint = _mount.State != null && _mount.State.AtPark
                            ? "\r\n\r\nMount is parked. Unpark before enabling tracking."
                            : "";
                        MessageBox.Show(this,
                            "Tracking command rejected by mount." +
                            (string.IsNullOrWhiteSpace(err) ? "" : "\r\nMount error: " + err) +
                            hint,
                            "Tracking", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        _suppressTrackingCheckEvent = true;
                        try { _trackingCheck.Checked = !_trackingCheck.Checked; }
                        finally { _suppressTrackingCheckEvent = false; }
                    }
                }
                catch (Exception ex)
                {
                    CopyableMessage.Show(this, "Tracking", "Tracking command failed:\r\n\r\n" + ex.ToString());
                }
            };
            _stateLabel = new StatusLabel { Left = 180, Top = 24, Width = 170, Height = 22 };
            _stateLabel.Kind = PulseDot.StatusKind.Neutral;
            _stateLabel.Text = "State: —";
            _slewingBadge = new SlewingBadge { Left = 180, Top = 24, Width = 170, Height = 22, Visible = false };

            _trackingModeBox = new ComboBox { Left = 110, Top = 58, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            _trackingModeBox.Items.AddRange(new object[] { "Sidereal", "Lunar", "Solar", "King" });
            _trackingModeBox.SelectedIndexChanged += (s, e) =>
            {
                if (_suppressTrackingModeEvent || !_hubConnected) return;
                switch ((string)_trackingModeBox.SelectedItem)
                {
                    case "Sidereal": _mount.Protocol.SetTrackingSidereal(); break;
                    case "Lunar":    _mount.Protocol.SetTrackingLunar();    break;
                    case "Solar":    _mount.Protocol.SetTrackingSolar();    break;
                    case "King":     _mount.Protocol.SetTrackingKing();     break;
                }
                // Suppress poll-based override for 3 s; gives mount time to accept the
                // command before the next :GU# cycle reads back and potentially reverts.
                _trackingModeSetAt = DateTime.UtcNow;
            };

            _guideRateBox = new NumericUpDown { Left = 110, Top = 92, Width = 60, Minimum = 0.01M, Maximum = 1.0M, DecimalPlaces = 2, Increment = 0.05M };
            _guideRateBox.ValueChanged += (s, e) => { if (_hubConnected) _mount.Protocol.SetGuideRateMultiplier((double)_guideRateBox.Value); };

            // Slew rate: slider + numeric bound one-to-one. Slider stores deg/s*100
            // as int; numeric is the source-of-truth value read by OnDirPress and
            // SaveSettings. On connect, bounds tighten to firmware's [base/2, base*2].
            _slewSpeedBox = new NumericUpDown { Left = 370, Top = 92, Width = 60, Minimum = 0.1M, Maximum = 10M, DecimalPlaces = 2, Increment = 0.25M };
            _slewSpeedSlider = new TrackBar {
                AutoSize = false,
                Left = 245, Top = 88, Width = 120, Height = 28,
                Minimum = 10, Maximum = 1000, TickStyle = TickStyle.None,
                SmallChange = 5, LargeChange = 25,
            };
            _slewSpeedSlider.ValueChanged += (s, e) =>
            {
                if (_suppressSlewSyncEvent) return;
                _suppressSlewSyncEvent = true;
                try { _slewSpeedBox.Value = Math.Max(_slewSpeedBox.Minimum, Math.Min(_slewSpeedBox.Maximum, _slewSpeedSlider.Value / 100M)); }
                finally { _suppressSlewSyncEvent = false; }
            };
            _slewSpeedBox.ValueChanged += (s, e) =>
            {
                if (_suppressSlewSyncEvent) return;
                _suppressSlewSyncEvent = true;
                try { _slewSpeedSlider.Value = Math.Max(_slewSpeedSlider.Minimum, Math.Min(_slewSpeedSlider.Maximum, (int)Math.Round(_slewSpeedBox.Value * 100M))); }
                finally { _suppressSlewSyncEvent = false; }
            };

            _meridianActionBox = new ComboBox { Left = 110, Top = 126, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
            _meridianActionBox.Items.AddRange(new object[] { "Auto Flip", "Stop at Meridian" });
            _meridianActionBox.SelectedIndexChanged += (s, e) => { if (_hubConnected) _mount.Protocol.SetMeridianAutoFlip(_meridianActionBox.SelectedIndex == 0); };

            // Advanced button exposes runtime-settable OnStepX options that don't
            // belong on the main form (preferred pier side, pause-at-home). Always
            // enabled — the dialog itself gates mount writes on _hubConnected and
            // still lets users edit persisted settings while offline.
            _advancedBtn = new FlatButton { Text = "Advanced...", Left = 280, Top = 125, Width = 100 };
            _advancedBtn.Click += (s, e) => OpenAdvancedSettings();

            g.Controls.Add(_trackingCheck);
            g.Controls.Add(_stateLabel);
            g.Controls.Add(_slewingBadge);
            g.Controls.Add(new Label { Text = "Tracking rate:", Left = 10, Top = 62, Width = 100 });
            g.Controls.Add(_trackingModeBox);
            g.Controls.Add(new Label { Text = "Guide rate (× sid):", Left = 10, Top = 96, Width = 100 });
            g.Controls.Add(_guideRateBox);
            g.Controls.Add(new Label { Text = "Slew (°/s):", Left = 185, Top = 96, Width = 60 });
            g.Controls.Add(_slewSpeedSlider);
            g.Controls.Add(_slewSpeedBox);
            g.Controls.Add(new Label { Text = "At meridian:", Left = 10, Top = 130, Width = 100 });
            g.Controls.Add(_meridianActionBox);
            g.Controls.Add(_advancedBtn);
            _mountActionControls.Add(_trackingCheck);
            _mountActionControls.Add(_trackingModeBox);
            _mountActionControls.Add(_guideRateBox);
            _mountActionControls.Add(_slewSpeedBox);
            _mountActionControls.Add(_slewSpeedSlider);
            _mountActionControls.Add(_meridianActionBox);
            _mountActionControls.Add(_advancedBtn);
            return g;
        }

        private void OpenAdvancedSettings()
        {
            using (var dlg = new AdvancedSettingsForm(_mount))
            {
                dlg.ShowDialog(this);
            }
        }

        private SectionPanel BuildLimitsGroup()
        {
            var g = NewGroup("Limits", 440, 150);
            _horizonLimitBox = new NumericUpDown { Left = 110, Top = 26, Width = 60, Minimum = -30, Maximum = 30 };
            _overheadLimitBox = new NumericUpDown { Left = 260, Top = 26, Width = 60, Minimum = 60, Maximum = 90, Value = 85 };
            // OnStepX meridian limits are minutes of RA (1 min = 0.25°). Typical
            // safe range is -270..+270; most users stay within ±60. Negative value
            // stops tracking before the meridian on that side.
            _meridianEastBox = new NumericUpDown { Left = 110, Top = 56, Width = 60, Minimum = -270, Maximum = 270 };
            _meridianWestBox = new NumericUpDown { Left = 260, Top = 56, Width = 60, Minimum = -270, Maximum = 270 };
            // Sync distance guardrail. Driver-side (not firmware) — confirmation
            // popup when an ASCOM sync would move the mount by more than this
            // many degrees. 0 disables. Lives in Limits group because it is
            // conceptually a safety limit on the sync operation.
            _syncLimitBox = new NumericUpDown { Left = 110, Top = 86, Width = 60, Minimum = 0, Maximum = 180, Value = 0 };
            _syncLimitBox.ValueChanged += (s, e) => DriverSettings.SyncLimitDeg = (int)_syncLimitBox.Value;
            _limitsWriteBtn = new FlatButton { Text = "Upload", Left = 230, Top = 116, Width = 100 };
            _limitsWriteBtn.Click += (s, e) => DoWriteLimits();
            g.Controls.Add(new Label { Text = "Horizon (°):", Left = 10, Top = 30, Width = 100 });
            g.Controls.Add(_horizonLimitBox);
            g.Controls.Add(new Label { Text = "Overhead (°):", Left = 180, Top = 30, Width = 80 });
            g.Controls.Add(_overheadLimitBox);
            g.Controls.Add(new Label { Text = "Merid. E (min RA):", Left = 10, Top = 60, Width = 100 });
            g.Controls.Add(_meridianEastBox);
            g.Controls.Add(new Label { Text = "Merid. W (min RA):", Left = 175, Top = 60, Width = 85 });
            g.Controls.Add(_meridianWestBox);
            g.Controls.Add(new Label { Text = "Sync limit (°):", Left = 10, Top = 90, Width = 100 });
            g.Controls.Add(_syncLimitBox);
            g.Controls.Add(new Label { Text = "0 = disabled", Left = 180, Top = 90, Width = 100, ForeColor = SystemColors.GrayText });
            g.Controls.Add(_limitsWriteBtn);
            _mountActionControls.Add(_horizonLimitBox);
            _mountActionControls.Add(_overheadLimitBox);
            _mountActionControls.Add(_meridianEastBox);
            _mountActionControls.Add(_meridianWestBox);
            _mountActionControls.Add(_limitsWriteBtn);
            _mountActionControls.Add(_syncLimitBox);
            return g;
        }

        private SectionPanel BuildPositionGroup()
        {
            var g = NewGroup("Current Position", 360, 195);
            _raLabel = new Label { Left = 110, Top = 28, Width = 230, Text = "—" };
            _decLabel = new Label { Left = 110, Top = 54, Width = 230, Text = "—" };
            _altLabel = new Label { Left = 110, Top = 80, Width = 230, Text = "—" };
            _azLabel = new Label { Left = 110, Top = 106, Width = 230, Text = "—" };
            _pierLabel = new Label { Left = 110, Top = 132, Width = 230, Text = "—" };
            // Diagnostic: mount-reported LST alongside computed sky LST for the
            // stored site longitude. A large Δ (> a few seconds) points at a
            // longitude-sign or UTC-offset convention mismatch — which directly
            // corrupts pier-side selection on slews (pier choice uses sign of
            // HA = LST − RA).
            _lstLabel = new Label { Left = 110, Top = 160, Width = 230, Text = "—" };
            g.Controls.Add(new Label { Text = "RA:", Left = 10, Top = 28, Width = 100 });
            g.Controls.Add(new Label { Text = "Dec:", Left = 10, Top = 54, Width = 100 });
            g.Controls.Add(new Label { Text = "Altitude:", Left = 10, Top = 80, Width = 100 });
            g.Controls.Add(new Label { Text = "Azimuth:", Left = 10, Top = 106, Width = 100 });
            g.Controls.Add(new Label { Text = "Pier side:", Left = 10, Top = 132, Width = 100 });
            g.Controls.Add(new Label { Text = "LST (mount / sky):", Left = 10, Top = 160, Width = 100 });
            g.Controls.Add(_raLabel);
            g.Controls.Add(_decLabel);
            g.Controls.Add(_altLabel);
            g.Controls.Add(_azLabel);
            g.Controls.Add(_pierLabel);
            g.Controls.Add(_lstLabel);
            return g;
        }

        // Compute apparent sidereal time (hours, 0..24) for the given west-positive
        // longitude at the current UTC. Mean GMST formula (Meeus, ch. 12) — accurate
        // to a few tenths of a second, plenty for diagnosing a sign-convention bug.
        private static double ComputeSkyLstHours(double westLonDeg)
        {
            var utc = DateTime.UtcNow;
            double jd = utc.ToOADate() + 2415018.5;
            double d = jd - 2451545.0;
            double t = d / 36525.0;
            double gmstDeg = 280.46061837 + 360.98564736629 * d + 0.000387933 * t * t - (t * t * t) / 38710000.0;
            double lstDeg = gmstDeg - westLonDeg;
            lstDeg = ((lstDeg % 360.0) + 360.0) % 360.0;
            return lstDeg / 15.0;
        }

        private static string FormatHours(double h)
        {
            h = ((h % 24.0) + 24.0) % 24.0;
            int hh = (int)h;
            double remMin = (h - hh) * 60.0;
            int mm = (int)remMin;
            double ss = (remMin - mm) * 60.0;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00.0}", hh, mm, ss);
        }

        private SectionPanel BuildParkHomeGroup()
        {
            var g = NewGroup("Park / Home / Go To", 360, 140);
            _parkBtn = new FlatButton { Text = "Park", Left = 10, Top = 26, Width = 80 };
            _unparkBtn = new FlatButton { Text = "Unpark", Left = 100, Top = 26, Width = 80 };
            // :hF# per OnStep command reference resets the telescope's home
            // reference to the current axes (mount must be physically at home).
            // Mirrors the "Set Home" action in the OnStepX web UI.
            _findHomeBtn = new FlatButton { Text = "Set Home", Left = 190, Top = 26, Width = 100 };
            _goHomeBtn = new FlatButton { Text = "Go Home", Left = 10, Top = 60, Width = 110 };
            // NOTE: this button sends :hQ# which sets the PARK position at the
            // current axes. It does NOT redefine the mount's home reference —
            // LX200 has no standard "set current as home" command; that calibration
            // is done via the OnStepX web UI / SHC. Previously mislabeled
            // "Reset Home (here)", which caused users to think GoHome would return
            // to this position (it does not, which surfaced as a park-vs-home offset).
            _resetHomeBtn = new FlatButton { Text = "Set Park Here", Left = 130, Top = 60, Width = 160 };
            _slewTargetBtn = new FlatButton { Text = "Slew to Target...", Left = 10, Top = 94, Width = 200 };
            ((FlatButton)_slewTargetBtn).Kind = FlatButton.Variant.Primary;
            _parkBtn.Click += (s, e) => Guard(() => ReportIfRejected("Park", _mount.Protocol.Park()));
            _unparkBtn.Click += (s, e) => Guard(() => ReportIfRejected("Unpark", _mount.Protocol.Unpark()));
            _findHomeBtn.Click += (s, e) => Guard(() => _mount.Protocol.FindHome());
            _goHomeBtn.Click += (s, e) => Guard(() => _mount.Protocol.GoHome());
            _resetHomeBtn.Click += (s, e) => Guard(() => _mount.Protocol.SetParkHere());
            _slewTargetBtn.Click += (s, e) => OpenSlewTarget();
            g.Controls.Add(_parkBtn); g.Controls.Add(_unparkBtn); g.Controls.Add(_findHomeBtn); g.Controls.Add(_goHomeBtn); g.Controls.Add(_resetHomeBtn);
            g.Controls.Add(_slewTargetBtn);
            _mountActionControls.Add(_parkBtn);
            _mountActionControls.Add(_unparkBtn);
            _mountActionControls.Add(_findHomeBtn);
            _mountActionControls.Add(_goHomeBtn);
            _mountActionControls.Add(_resetHomeBtn);
            _mountActionControls.Add(_slewTargetBtn);
            return g;
        }

        private void OpenSlewTarget()
        {
            using (var f = new SlewTargetForm(_mount)) f.ShowDialog(this);
        }

        private SectionPanel BuildSlewPadGroup()
        {
            var g = NewGroup("Manual Slew", 360, 260);
            _slewPad = new SlewPadControl { Left = 20, Top = 24, Width = 320, Height = 220 };
            _slewPad.DirectionPressed += OnDirPress;
            _slewPad.DirectionReleased += OnDirRelease;
            _slewPad.Stop += () => Guard(() => _mount.Protocol.AbortSlew());
            g.Controls.Add(_slewPad);
            _mountActionControls.Add(_slewPad);
            return g;
        }

        private Panel BuildFooter()
        {
            var p = new Panel { Width = 360, Height = 30, BackColor = Color.Transparent };
            _clientsStatus = new StatusLabel { Left = 0, Top = 4, Width = 360, Height = 22 };
            _clientsStatus.Kind = PulseDot.StatusKind.Info;
            _clientsStatus.Pulsing = false;
            _clientsStatus.Text = "Connected clients: 0";
            p.Controls.Add(_clientsStatus);
            return p;
        }

        private Panel BuildAppHeader()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(12, 6, 10, 6) };
            header.Paint += (s, e) =>
            {
                var g = e.Graphics;
                var p = Theme.P;
                using (var br = new SolidBrush(p.Panel))
                    g.FillRectangle(br, header.ClientRectangle);
                using (var pen = new Pen(p.Border, 1))
                    g.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
            };

            var logo = new AccentGlyph { Left = 12, Top = 11, Width = 20, Height = 20 };

            var title = new Label
            {
                Left = 38, Top = 10, AutoSize = true, BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10f, FontStyle.Regular),
                Text = "OnStepX ASCOM Driver",
            };
            var versionLbl = new Label
            {
                Left = title.Left + 180, Top = 12, AutoSize = true, BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                Text = "\u00B7 " + GetVersionString(),
            };
            title.Paint += (s, e) => versionLbl.Left = title.Right + 4;

            var toggle = new ThemeToggle
            {
                Width = 62, Height = 28, Top = 7, Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            toggle.Left = header.Width - toggle.Width - 10;
            header.Resize += (s, e) => toggle.Left = header.Width - toggle.Width - 10;

            header.Controls.Add(logo);
            header.Controls.Add(title);
            header.Controls.Add(versionLbl);
            header.Controls.Add(toggle);
            return header;
        }

        // Small telescope glyph in the app header, tinted with the accent color.
        private sealed class AccentGlyph : Control
        {
            public AccentGlyph()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint
                       | ControlStyles.OptimizedDoubleBuffer
                       | ControlStyles.ResizeRedraw
                       | ControlStyles.UserPaint
                       | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
                Theme.Changed += (s, e) => Invalidate();
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var p = Theme.P;
                using (var pen = new Pen(p.Accent, 1.6f)
                {
                    StartCap = System.Drawing.Drawing2D.LineCap.Round,
                    EndCap = System.Drawing.Drawing2D.LineCap.Round,
                    LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
                })
                {
                    // stylised telescope: eyepiece tube + mount arm
                    float s = Math.Min(Width, Height) / 24f;
                    float cx = Width / 2f, cy = Height / 2f;
                    PointF P(float x, float y) => new PointF(cx + (x - 12) * s, cy + (y - 12) * s);
                    g.DrawLines(pen, new[] { P(3, 14), P(11, 11), P(14, 19), P(6, 22), P(3, 14) });
                    g.DrawLine(pen, P(11, 11).X, P(11, 11).Y, P(19, 8).X, P(19, 8).Y);
                    g.DrawLine(pen, P(15, 3).X, P(15, 3).Y, P(18, 11).X, P(18, 11).Y);
                    g.DrawLine(pen, P(9, 20).X, P(9, 20).Y, P(11, 19).X, P(11, 19).Y);
                    g.DrawLine(pen, P(13, 22).X, P(13, 22).Y, P(15, 18).X, P(15, 18).Y);
                }
            }
        }

        private static SectionPanel NewGroup(string title, int w, int h)
            => new SectionPanel { Title = title, Width = w, Height = h + 42, Margin = new Padding(0, 0, 0, 8) };

        private static string GetVersionString()
        {
            try
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                if (v == null) return "v0.0.0";
                return "v" + v.Major + "." + v.Minor + "." + v.Build;
            }
            catch { return "v0.0.0"; }
        }

        // Walk the form tree and set BackColor/ForeColor per palette; certain native
        // controls (ComboBox, NumericUpDown, DateTimePicker, TrackBar) don't support
        // full dark theming — we still set reasonable colors on them.
        private void ApplyFormTheme()
        {
            var p = Theme.P;
            BackColor = p.Bg;
            ForeColor = p.Text;
            ApplyThemeRecursive(this, p);
            Invalidate(true);
        }

        private static void ApplyThemeRecursive(Control parent, ThemePalette p)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is SectionPanel sp)
                {
                    sp.BackColor = p.Panel2;
                }
                else if (c is FlatButton) { /* self-themed */ }
                else if (c is ThemedCheckBox cbt)
                {
                    cbt.BackColor = Color.Transparent;
                    cbt.ForeColor = p.Text;
                }
                else if (c is StatusLabel) { /* self-themed */ }
                else if (c is SlewingBadge) { /* self-themed */ }
                else if (c is PulseDot) { /* self-themed */ }
                else if (c is ConsoleLogBox) { /* self-themed */ }
                else if (c is DarkTextBox) { /* self-themed */ }
                else if (c is TextBox tb)
                {
                    tb.BackColor = p.InputBg;
                    tb.ForeColor = p.Text;
                    tb.BorderStyle = BorderStyle.FixedSingle;
                }
                else if (c is ComboBox cb)
                {
                    cb.FlatStyle = FlatStyle.Flat;
                    cb.BackColor = p.InputBg;
                    cb.ForeColor = p.Text;
                }
                else if (c is NumericUpDown nud)
                {
                    nud.BackColor = p.InputBg;
                    nud.ForeColor = p.Text;
                    nud.BorderStyle = BorderStyle.FixedSingle;
                }
                else if (c is DateTimePicker dtp)
                {
                    dtp.CalendarForeColor = p.Text;
                    dtp.CalendarMonthBackground = p.Panel;
                    dtp.CalendarTitleBackColor = p.Panel2;
                    dtp.CalendarTitleForeColor = p.Text;
                    dtp.CalendarTrailingForeColor = p.TextFaint;
                }
                else if (c is TrackBar tbar)
                {
                    tbar.BackColor = p.Panel2;
                }
                else if (c is RichTextBox rtb)
                {
                    rtb.BackColor = p.ConsoleBg;
                    rtb.ForeColor = p.Text;
                }
                else if (c is ListView lv)
                {
                    lv.BackColor = p.Panel;
                    lv.ForeColor = p.Text;
                }
                else if (c is GroupBox gb)
                {
                    gb.BackColor = p.Panel2;
                    gb.ForeColor = p.Text;
                }
                else if (c is Button b)
                {
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderColor = p.InputBorder;
                    b.BackColor = p.BtnBg;
                    b.ForeColor = p.Text;
                }
                else if (c is CheckBox chk)
                {
                    chk.BackColor = Color.Transparent;
                    chk.ForeColor = p.Text;
                }
                else if (c is Label lb)
                {
                    // Grey-ish labels keep a muted color; regular labels stay readable.
                    if (lb.ForeColor == SystemColors.GrayText || lb.ForeColor == Color.DimGray)
                        lb.ForeColor = p.TextFaint;
                    else
                        lb.ForeColor = p.Text;
                    lb.BackColor = Color.Transparent;
                }
                else if (c is Panel)
                {
                    // Leave panel backgrounds alone unless they explicitly tag themselves.
                }

                if (c.HasChildren) ApplyThemeRecursive(c, p);
            }
        }

        private Panel BuildLogPanel()
        {
            _logPanel = new Panel { Dock = DockStyle.Bottom, Height = ConsoleExpandedHeight, Padding = new Padding(8, 0, 8, 8) };
            _logPanel.BackColor = Theme.P.Bg;

            var ioPane = BuildIoPane();
            ioPane.Dock = DockStyle.Fill;

            var header = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = Color.Transparent };
            _consoleToggle = new ThemedCheckBox { Text = "Show console", Left = 0, Top = 8, Width = 130, Checked = true };
            _consoleToggle.CheckedChanged += (s, e) => ApplyConsoleVisibility();
            var cmdLabel = new Label { Text = "Manual cmd:", Left = 136, Top = 10, Width = 80, BackColor = Color.Transparent, ForeColor = Theme.P.Text };
            _cmdInput = new TextBox { Left = 218, Top = 6, Width = 256, Font = new Font("Consolas", 9f), BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.P.InputBg, ForeColor = Theme.P.Text };
            _cmdInput.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { SendManualCommand(); e.Handled = true; e.SuppressKeyPress = true; }
            };
            _cmdSendBtn = new FlatButton { Text = "Send", Left = 478, Top = 4, Width = 70, Height = 26 };
            ((FlatButton)_cmdSendBtn).Kind = FlatButton.Variant.Primary;
            _cmdSendBtn.Click += (s, e) => SendManualCommand();
            var hint = new Label { Text = "(e.g. :GVP#  — leading ':' and trailing '#' auto-added)", Left = 556, Top = 10, Width = 350, ForeColor = Theme.P.TextFaint, BackColor = Color.Transparent };
            header.Controls.Add(_consoleToggle);
            header.Controls.Add(cmdLabel);
            header.Controls.Add(_cmdInput);
            header.Controls.Add(_cmdSendBtn);
            header.Controls.Add(hint);

            Theme.Changed += (s, e) =>
            {
                _logPanel.BackColor = Theme.P.Bg;
                cmdLabel.ForeColor = Theme.P.Text;
                _cmdInput.BackColor = Theme.P.InputBg;
                _cmdInput.ForeColor = Theme.P.Text;
                hint.ForeColor = Theme.P.TextFaint;
            };

            _logPanel.Controls.Add(ioPane);
            _logPanel.Controls.Add(header);
            return _logPanel;
        }

        private void ApplyConsoleVisibility()
        {
            bool show = _consoleToggle.Checked;
            // Header is always visible; toggle only the IO pane height.
            _logPanel.Height = show ? ConsoleExpandedHeight : ConsoleCollapsedHeight;
            foreach (Control c in _logPanel.Controls)
                if (c.Dock == DockStyle.Fill) c.Visible = show;
        }

        private void SendManualCommand()
        {
            if (!_hubConnected)
            {
                MessageBox.Show(this, "Not connected.", "Manual command", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string raw = (_cmdInput.Text ?? "").Trim();
            if (raw.Length == 0) return;
            if (!raw.StartsWith(":")) raw = ":" + raw;
            if (!raw.EndsWith("#"))   raw = raw + "#";
            string cmd = raw;
            _cmdSendBtn.Enabled = false;
            System.Threading.Tasks.Task.Run(() =>
            {
                string reply = null; Exception err = null;
                try { reply = _mount.SendAndReceiveRaw(cmd); }
                catch (Exception ex) { err = ex; }
                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke(new Action(() =>
                    {
                        _cmdSendBtn.Enabled = true;
                        if (err != null)
                            CopyableMessage.Show(this, "Manual command", "Command '" + cmd + "' failed:\r\n\r\n" + err.ToString());
                        else
                            _cmdInput.SelectAll();
                    }));
                }
                catch { }
            });
        }

        // Paired command -> response view plus diagnostic notes. One line per
        // exchange or note. Filter is a case-sensitive, whitespace-tokenised
        // substring match — every token must appear literally in the line.
        private Panel BuildIoPane()
        {
            var pane = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Theme.P.ConsoleBg, Padding = new Padding(1) };

            // Two-row header. Row 1: title/toggles/log-level actions. Row 2:
            // filter field with its own clear button. Separated rows so Clear/Copy
            // on row 1 can't be mistaken for filter controls.
            var header = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Theme.P.ConsoleLine, Padding = new Padding(8, 4, 8, 4) };
            var title = new Label { Text = "CONSOLE", Left = 8, Top = 8, Width = 80, Font = new Font("Segoe UI", 8f, FontStyle.Bold), ForeColor = Theme.P.TextDim, BackColor = Color.Transparent };
            _ioEnable = new ThemedCheckBox { Text = "Enabled", Left = 94, Top = 8, Width = 80, Checked = true };
            _ioAutoScroll = new ThemedCheckBox { Text = "Auto-scroll", Left = 180, Top = 8, Width = 110, Checked = true };
            _ioAutoScroll.CheckedChanged += (s, e) =>
            {
                if (_ioAutoScroll.Checked && _ioBox != null)
                {
                    _ioBox.SelectionStart = _ioBox.TextLength;
                    _ioBox.ScrollToCaret();
                }
            };
            var clearBtn = new FlatButton { Text = "Clear", Left = 540, Top = 4, Width = 70, Height = 24, Sz = FlatButton.ButtonSize.Small };
            var copyBtn  = new FlatButton { Text = "Copy",  Left = 616, Top = 4, Width = 70, Height = 24, Sz = FlatButton.ButtonSize.Small };
            clearBtn.Click += (s, e) => { _ioBox.Clear(); _ioAll.Clear(); while (_ioPending.TryDequeue(out _)) { } };
            copyBtn.Click  += (s, e) => { try { if (!string.IsNullOrEmpty(_ioBox.Text)) Clipboard.SetText(_ioBox.Text); } catch { } };

            var filterLabel = new Label { Text = "Filter:", Left = 8, Top = 36, Width = 46, ForeColor = Theme.P.TextDim, BackColor = Color.Transparent };
            _ioFilter = new TextBox { Left = 56, Top = 31, Width = 546, Font = new Font("Consolas", 9f), BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.P.InputBg, ForeColor = Theme.P.Text };
            _ioFilter.TextChanged += (s, e) => RebuildIoView();
            var filterClearBtn = new FlatButton { Text = "Clear filter", Left = 608, Top = 29, Width = 90, Height = 24, Sz = FlatButton.ButtonSize.Small };
            filterClearBtn.Click += (s, e) => { _ioFilter.Clear(); _ioFilter.Focus(); };

            header.Controls.Add(title);
            header.Controls.Add(_ioEnable);
            header.Controls.Add(_ioAutoScroll);
            header.Controls.Add(clearBtn);
            header.Controls.Add(copyBtn);
            header.Controls.Add(filterLabel);
            header.Controls.Add(_ioFilter);
            header.Controls.Add(filterClearBtn);

            _ioBox = new ConsoleLogBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 9f) };
            pane.Controls.Add(_ioBox);
            pane.Controls.Add(header);

            Theme.Changed += (s, e) =>
            {
                pane.BackColor = Theme.P.ConsoleBg;
                header.BackColor = Theme.P.ConsoleLine;
                title.ForeColor = Theme.P.TextDim;
                filterLabel.ForeColor = Theme.P.TextDim;
                _ioFilter.BackColor = Theme.P.InputBg;
                _ioFilter.ForeColor = Theme.P.Text;
            };

            return pane;
        }

        // ---------- UI actions ----------
        private void DoAutoDetect()
        {
            _autoDetectBtn.Enabled = false; _autoDetectBtn.Text = "Scanning…";
            try
            {
                var port = PortAutoDetect.FindOnStepPort();
                if (port != null) { _transportKind.SelectedItem = "Serial"; _portCombo.Text = port; MessageBox.Show(this, "Found OnStepX on " + port); }
                else MessageBox.Show(this, "No OnStepX mount detected on serial ports.");
            }
            finally { _autoDetectBtn.Enabled = true; _autoDetectBtn.Text = "Auto-Detect"; }
        }

        public void RequestConnect() => DoConnect();
        public void RequestDisconnect() => DoDisconnect();

        private void DoConnect()
        {
            // Snapshot UI state on main thread; open on a worker so the form stays painted
            // and responsive while the ESP32 boot-probe loop runs (indefinite — user aborts
            // via Disconnect, which cancels the token and tears down the transport).
            string kind = (string)_transportKind.SelectedItem;
            string host = _hostBox.Text;
            int tcpPort = (int)_tcpPortBox.Value;
            string port = _portCombo.Text;
            int baud = (int)_baudBox.Value;

            ApplyConnState(ConnState.Connecting);

            var cts = new System.Threading.CancellationTokenSource();
            try { _connectCts?.Cancel(); _connectCts?.Dispose(); } catch { }
            _connectCts = cts;

            System.Threading.Tasks.Task.Run(() =>
            {
                Exception err = null;
                bool canceled = false;
                double mountBaseSlew = 0.0;
                try
                {
                    ITransport t = kind == "TCP"
                        ? (ITransport)new TcpTransport(host, tcpPort)
                        : new SerialTransport(port, baud);
                    _mount.Configure(t);
                    // Open() stages: transport up -> ConnectionChanged (UI holds Connecting),
                    // then :GVP# probe loop until responsive -> ConnectionChanged (UI flips to
                    // Connected via SyncConnectionStateFromMount). Throws OCE on cancellation.
                    _mount.Open(cts.Token);
                    // Firmware clamps :SX92 us/step to [base/2, base*2], so the practical
                    // slew rate range is [0.5*base, 2*base] deg/s derived from
                    // SLEW_RATE_BASE_DESIRED. :GX97# cold-boot quirk is handled inside
                    // GetCurrentStepRateDegPerSec.
                    try { mountBaseSlew = _mount.Protocol.GetBaseSlewRateDegPerSec(); } catch { }
                }
                catch (OperationCanceledException) { canceled = true; }
                catch (Exception ex) { err = ex; }

                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke(new Action(() =>
                    {
                        if (canceled)
                        {
                            ApplyConnState(ConnState.Disconnected);
                            return;
                        }
                        if (err != null)
                        {
                            ApplyConnState(ConnState.Disconnected);
                            CopyableMessage.Show(this, "Connect failed", err.ToString());
                            return;
                        }
                        _hubConnected = true;
                        SaveSettings();

                        // Retune slew controls to mount's reported range [base/2, base*2].
                        // If the mount doesn't expose base (returns 0), leave UI limits
                        // alone and let the firmware silently clamp at runtime.
                        if (mountBaseSlew > 0.1)
                        {
                            decimal minDec = (decimal)Math.Round(mountBaseSlew * 0.5, 2);
                            decimal maxDec = (decimal)Math.Round(mountBaseSlew * 2.0, 2);
                            _suppressSlewSyncEvent = true;
                            try
                            {
                                _slewSpeedBox.Minimum = minDec;
                                _slewSpeedBox.Maximum = maxDec;
                                if (_slewSpeedBox.Value < minDec) _slewSpeedBox.Value = minDec;
                                if (_slewSpeedBox.Value > maxDec) _slewSpeedBox.Value = maxDec;
                                _slewSpeedSlider.Minimum = (int)Math.Round(minDec * 100M);
                                _slewSpeedSlider.Maximum = (int)Math.Round(maxDec * 100M);
                                _slewSpeedSlider.Value = (int)Math.Round(_slewSpeedBox.Value * 100M);
                            }
                            finally { _suppressSlewSyncEvent = false; }
                        }

                        ApplyConnState(ConnState.Connected);
                        ReconcileSiteLocationOnConnect();
                        if (DriverSettings.AutoSyncTimeOnConnect)
                        {
                            try { DoSyncTime(); TransportLogger.Note("Auto-synced date/time from PC on connect"); }
                            catch (Exception syncEx) { TransportLogger.Note("Auto-sync time failed: " + syncEx.Message); }
                        }
                        ReapplyAdvancedSettingsOnConnect();
                    }));
                }
                catch { }
                finally
                {
                    if (ReferenceEquals(_connectCts, cts))
                        _connectCts = null;
                    try { cts.Dispose(); } catch { }
                }
            });
        }

        // Re-apply advanced pier/flip settings from DriverSettings to the mount.
        // Needed because OnStepX *_MEMORY flags are typically OFF (compile-time),
        // so runtime values revert on power cycle. Silent on failure — these are
        // non-critical and some firmware builds may refuse sub-commands.
        private void ReapplyAdvancedSettingsOnConnect()
        {
            try
            {
                var pref = DriverSettings.PreferredPierSide;
                LX200Protocol.PreferredPier pierEnum;
                switch (string.IsNullOrEmpty(pref) ? 'B' : char.ToUpperInvariant(pref[0]))
                {
                    case 'E': pierEnum = LX200Protocol.PreferredPier.East; break;
                    case 'W': pierEnum = LX200Protocol.PreferredPier.West; break;
                    case 'A': pierEnum = LX200Protocol.PreferredPier.Auto; break;
                    default:  pierEnum = LX200Protocol.PreferredPier.Best; break;
                }
                _mount.Protocol.SetPreferredPierSide(pierEnum);
                _mount.Protocol.SetPauseAtHomeOnFlip(DriverSettings.PauseAtHomeOnFlip);
                TransportLogger.Note("Re-applied advanced settings (preferred pier=" + pref + ", pauseHome=" + DriverSettings.PauseAtHomeOnFlip + ")");

                // Probe: slew rate family. :GX92# current us/step, :GX93# base us/step
                // (derived from SLEW_RATE_BASE_DESIRED), :GX97# current deg/s, :GX99#
                // mechanical lower-limit us/step. base deg/s = curDegPerSec * (usCur / usBase).
                try
                {
                    double usCur  = _mount.Protocol.GetUsPerStepCurrent();
                    double usBase = _mount.Protocol.GetUsPerStepBase();
                    double curDps = _mount.Protocol.GetCurrentStepRateDegPerSec();
                    double usLim  = _mount.Protocol.GetUsPerStepLowerLimit();
                    double baseDps = _mount.Protocol.GetBaseSlewRateDegPerSec();
                    TransportLogger.Note(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "Slew rate probe: :GX92#={0:0.000} us/step cur ; :GX93#={1:0.000} us/step base ; :GX97#={2:0.###} deg/s cur ; :GX99#={3:0.000} us/step limit ; derived base={4:0.###} deg/s",
                        usCur, usBase, curDps, usLim, baseDps));
                }
                catch (Exception probeEx)
                {
                    TransportLogger.Note("Slew rate probe failed: " + probeEx.Message);
                }
            }
            catch (Exception ex)
            {
                TransportLogger.Note("Re-apply advanced settings failed: " + ex.Message);
            }
        }

        // Compare hub-stored site with mount site; if differ, ask user which to keep.
        private void ReconcileSiteLocationOnConnect()
        {
            double hubLat = DriverSettings.SiteLatitude;
            double hubLon = DriverSettings.SiteLongitude;
            double hubEle = DriverSettings.SiteElevation;

            double mLat = 0, mLon = 0, mEle = 0;
            string latRaw = "", lonRaw = "";
            bool ok = false;
            Exception lastEx = null;
            // Retry: right after connect the background poll and this call share the wire.
            // A timed-out or stale reply can leak bytes into the next read; one retry after
            // a short settle is enough to resync in practice.
            for (int attempt = 0; attempt < 3 && !ok; attempt++)
            {
                try
                {
                    latRaw = _mount.Protocol.GetLatitude();
                    lonRaw = _mount.Protocol.GetLongitudeRaw();
                    if (!CoordFormat.TryParseDegrees(latRaw, out mLat))
                        throw new FormatException("latitude reply '" + latRaw + "'");
                    if (!CoordFormat.TryParseDegrees(lonRaw, out mLon))
                        throw new FormatException("longitude reply '" + lonRaw + "'");
                    double.TryParse(StripReply(_mount.Protocol.GetElevation()),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out mEle);
                    ok = true;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    System.Threading.Thread.Sleep(250);
                }
            }
            if (!ok)
            {
                CopyableMessage.Show(this, "Site check",
                    "Could not read site from mount:\r\n\r\n" + (lastEx?.Message ?? "unknown") +
                    "\r\n\r\nRaw latitude reply:  " + FormatReply(latRaw) +
                    "\r\nRaw longitude reply: " + FormatReply(lonRaw) +
                    "\r\n\r\nIf the bytes look like random high-byte values, the baud rate is\r\n" +
                    "likely wrong or the adapter is still pulsing DTR/RTS on open.");
                return;
            }

            const double latLonTolDeg = 1.0 / 60.0; // 1 arcmin
            const double eleTolM = 10.0;
            bool differs = Math.Abs(mLat - hubLat) > latLonTolDeg
                        || Math.Abs(mLon - hubLon) > latLonTolDeg
                        || Math.Abs(mEle - hubEle) > eleTolM;

            if (!differs)
            {
                _latBox.Text = CoordFormat.FormatLatitudeDms(mLat);
                _lonBox.Text = CoordFormat.FormatLongitudeDms(mLon);
                _eleBox.Text = mEle.ToString("F1", CultureInfo.InvariantCulture);
                return;
            }

            string msg =
                "Mount site location differs from the hub.\r\n\r\n" +
                "Mount:\r\n" +
                "   Lat " + CoordFormat.FormatLatitudeDms(mLat) + "\r\n" +
                "   Lon " + CoordFormat.FormatLongitudeDms(mLon) + "\r\n" +
                "   Ele " + mEle.ToString("F1", CultureInfo.InvariantCulture) + " m\r\n\r\n" +
                "Hub:\r\n" +
                "   Lat " + CoordFormat.FormatLatitudeDms(hubLat) + "\r\n" +
                "   Lon " + CoordFormat.FormatLongitudeDms(hubLon) + "\r\n" +
                "   Ele " + hubEle.ToString("F1", CultureInfo.InvariantCulture) + " m\r\n\r\n" +
                "Yes  = use Mount location (update hub)\r\n" +
                "No   = use Hub location (write to mount)\r\n" +
                "Cancel = keep both as-is";

            var r = MessageBox.Show(this, msg, "Site location mismatch",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Yes)
            {
                _latBox.Text = CoordFormat.FormatLatitudeDms(mLat);
                _lonBox.Text = CoordFormat.FormatLongitudeDms(mLon);
                _eleBox.Text = mEle.ToString("F1", CultureInfo.InvariantCulture);
                DriverSettings.SiteLatitude = mLat;
                DriverSettings.SiteLongitude = mLon;
                DriverSettings.SiteElevation = mEle;
            }
            else if (r == DialogResult.No)
            {
                try
                {
                    _mount.Protocol.SetLatitude(hubLat);
                    _mount.Protocol.SetLongitude(hubLon);
                    _mount.Protocol.SetElevation(hubEle);
                }
                catch (Exception ex)
                {
                    CopyableMessage.Show(this, "Site sync", "Failed to write site to mount:\r\n\r\n" + ex.ToString());
                }
            }
        }

        private static string StripReply(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.TrimEnd('#').Trim();
        }

        // Render a mount reply so binary noise is unambiguous in error dialogs: each byte
        // is shown as its printable char (when >= 0x20 and < 0x7F) plus a hex dump.
        private static string FormatReply(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(empty)";
            var sb = new System.Text.StringBuilder();
            sb.Append("'");
            foreach (char c in s) sb.Append(c >= 0x20 && c < 0x7F ? c : '.');
            sb.Append("'  hex: ");
            foreach (char c in s) sb.AppendFormat("{0:X2} ", (int)c & 0xFF);
            return sb.ToString().TrimEnd();
        }

        private void DoDisconnect()
        {
            // Cancel first so an in-flight probe loop unblocks before Close() runs.
            try { _connectCts?.Cancel(); } catch { }
            try { _mount.Close(); } catch { }
            _hubConnected = false;
            ApplyConnState(ConnState.Disconnected);
        }

        private void ApplyConnState(ConnState s)
        {
            _connState = s;
            switch (s)
            {
                case ConnState.Disconnected:
                    _statusLed.Text = "Disconnected";
                    _statusLed.Kind = PulseDot.StatusKind.Err;
                    _statusLed.Pulsing = false;
                    _connectBtn.Enabled = true;
                    _disconnectBtn.Enabled = false;
                    foreach (var c in _connectionControls) c.Enabled = true;
                    foreach (var c in _mountActionControls) c.Enabled = false;
                    foreach (var c in _disableWhileConnectingControls) c.Enabled = true;
                    if (_cmdInput != null) _cmdInput.Enabled = false;
                    if (_cmdSendBtn != null) _cmdSendBtn.Enabled = false;
                    if (_slewingBadge != null) _slewingBadge.Visible = false;
                    if (_stateLabel != null)
                    {
                        _stateLabel.Visible = true;
                        _stateLabel.Text = "State: \u2014";
                        _stateLabel.Kind = PulseDot.StatusKind.Neutral;
                        _stateLabel.Pulsing = false;
                    }
                    break;
                case ConnState.Connecting:
                    _statusLed.Text = "Connecting...";
                    _statusLed.Kind = PulseDot.StatusKind.Warn;
                    _statusLed.Pulsing = false;
                    _connectBtn.Enabled = false;
                    _disconnectBtn.Enabled = false;
                    foreach (var c in _connectionControls) c.Enabled = false;
                    foreach (var c in _mountActionControls) c.Enabled = false;
                    foreach (var c in _disableWhileConnectingControls) c.Enabled = false;
                    if (_cmdInput != null) _cmdInput.Enabled = false;
                    if (_cmdSendBtn != null) _cmdSendBtn.Enabled = false;
                    break;
                case ConnState.Connected:
                    _statusLed.Text = "Connected";
                    _statusLed.Kind = PulseDot.StatusKind.Ok;
                    _statusLed.Pulsing = true;
                    _connectBtn.Enabled = false;
                    _disconnectBtn.Enabled = true;
                    foreach (var c in _connectionControls) c.Enabled = false;
                    foreach (var c in _mountActionControls) c.Enabled = true;
                    foreach (var c in _disableWhileConnectingControls) c.Enabled = true;
                    if (_cmdInput != null) _cmdInput.Enabled = true;
                    if (_cmdSendBtn != null) _cmdSendBtn.Enabled = true;
                    break;
            }
        }

        private void DoWriteSite()
        {
            if (!_hubConnected)
            {
                MessageBox.Show(this, "Not connected.", "Write site", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            double lat, lon, ele;
            try
            {
                if (!CoordFormat.TryParseDegrees(_latBox.Text, out lat))
                    throw new FormatException("Latitude '" + _latBox.Text + "' is not a valid DMS value (e.g. +32°45'12\").");
                if (!CoordFormat.TryParseDegrees(_lonBox.Text, out lon))
                    throw new FormatException("Longitude '" + _lonBox.Text + "' is not a valid DMS value (e.g. -117°09'30\").");
                if (!double.TryParse(_eleBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out ele))
                    throw new FormatException("Elevation '" + _eleBox.Text + "' is not a valid number (metres).");
            }
            catch (Exception ex)
            {
                CopyableMessage.Show(this, "Write site", ex.Message);
                return;
            }

            try
            {
                bool latOk = _mount.Protocol.SetLatitude(lat);
                bool lonOk = _mount.Protocol.SetLongitude(lon);
                bool eleOk = _mount.Protocol.SetElevation(ele);
                if (!latOk || !lonOk || !eleOk)
                {
                    string err = "";
                    try { err = _mount.Protocol.GetLastError(); } catch { }
                    CopyableMessage.Show(this, "Upload site",
                        "Mount rejected one or more site values.\r\n" +
                        "  Latitude:  " + (latOk ? "OK" : "REJECTED") + "\r\n" +
                        "  Longitude: " + (lonOk ? "OK" : "REJECTED") + "\r\n" +
                        "  Elevation: " + (eleOk ? "OK" : "REJECTED") +
                        (string.IsNullOrWhiteSpace(err) ? "" : "\r\n\r\nMount error: " + err));
                    return;
                }
                DriverSettings.SiteLatitude = lat;
                DriverSettings.SiteLongitude = lon;
                DriverSettings.SiteElevation = ele;
                MessageBox.Show(this, "Site uploaded to mount.", "Upload site",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                CopyableMessage.Show(this, "Upload site", "Upload failed:\r\n\r\n" + ex.ToString());
            }
        }

        private void DoSyncLocationFromPc()
        {
            _siteSyncPcBtn.Enabled = false;
            var originalText = _siteSyncPcBtn.Text;
            _siteSyncPcBtn.Text = "Locating...";
            try
            {
                using (var watcher = new System.Device.Location.GeoCoordinateWatcher(System.Device.Location.GeoPositionAccuracy.High))
                {
                    watcher.MovementThreshold = 1.0;
                    if (!watcher.TryStart(false, TimeSpan.FromSeconds(15)))
                    {
                        MessageBox.Show(this,
                            "Windows Location service is unavailable or disabled.\r\n" +
                            "Enable it in Settings → Privacy & security → Location, and allow desktop apps.",
                            "Location unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    var c = watcher.Position.Location;
                    int waited = 0;
                    while ((c.IsUnknown || double.IsNaN(c.Latitude)) && waited < 10000)
                    {
                        System.Threading.Thread.Sleep(250);
                        Application.DoEvents();
                        c = watcher.Position.Location;
                        waited += 250;
                    }
                    if (c.IsUnknown)
                    {
                        MessageBox.Show(this, "Could not obtain a location fix within 10 seconds.",
                            "Location unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    _latBox.Text = CoordFormat.FormatLatitudeDms(c.Latitude);
                    _lonBox.Text = CoordFormat.FormatLongitudeDms(c.Longitude);
                    double ele = double.IsNaN(c.Altitude) ? 0.0 : c.Altitude;
                    _eleBox.Text = ele.ToString("F1", CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex)
            {
                CopyableMessage.Show(this, "Location error", "Location lookup failed:\r\n\r\n" + ex.ToString());
            }
            finally
            {
                _siteSyncPcBtn.Text = originalText;
                _siteSyncPcBtn.Enabled = true;
            }
        }

        private void TickLocalTime()
        {
            if (_datePicker.Focused || _timePicker.Focused) return;
            var now = DateTime.Now;
            _datePicker.Value = now.Date;
            _timePicker.Value = now;
        }

        private void DoSyncTime()
        {
            if (!_hubConnected) return;
            var now = DateTime.Now;
            double offsetH = (now - now.ToUniversalTime()).TotalHours;
            _mount.Protocol.SetUtcOffset(offsetH);
            _mount.Protocol.SetLocalDate(now);
            _mount.Protocol.SetLocalTime(now);
            _utcOffsetBox.Value = (decimal)Math.Round(offsetH, 1);
            _datePicker.Value = now.Date; _timePicker.Value = now;
        }

        // Push the date/time/timezone currently shown in the UI to the mount.
        // Order matters: offset first so the subsequent LocalDate/LocalTime are
        // interpreted in the intended timezone.
        private void DoWriteDateTime()
        {
            if (!_hubConnected) return;
            double offsetH = (double)_utcOffsetBox.Value;
            var local = _datePicker.Value.Date
                .AddHours(_timePicker.Value.Hour)
                .AddMinutes(_timePicker.Value.Minute)
                .AddSeconds(_timePicker.Value.Second);
            try
            {
                bool offOk  = _mount.Protocol.SetUtcOffset(offsetH);
                bool dateOk = _mount.Protocol.SetLocalDate(local);
                bool timeOk = _mount.Protocol.SetLocalTime(local);
                if (!offOk || !dateOk || !timeOk)
                {
                    string err = "";
                    try { err = _mount.Protocol.GetLastError(); } catch { }
                    CopyableMessage.Show(this, "Upload date/time",
                        "Mount rejected one or more values.\r\n" +
                        "  UTC offset: " + (offOk  ? "OK" : "REJECTED") + "\r\n" +
                        "  Date:       " + (dateOk ? "OK" : "REJECTED") + "\r\n" +
                        "  Time:       " + (timeOk ? "OK" : "REJECTED") +
                        (string.IsNullOrWhiteSpace(err) ? "" : "\r\n\r\nMount error: " + err));
                    return;
                }
                MessageBox.Show(this, "Date/time uploaded to mount.", "Upload date/time",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                CopyableMessage.Show(this, "Upload date/time", "Upload failed:\r\n\r\n" + ex.ToString());
            }
        }

        private void DoWriteLimits()
        {
            if (!_hubConnected) return;
            try
            {
                bool hOk = _mount.Protocol.SetHorizonLimit((int)_horizonLimitBox.Value);
                bool oOk = _mount.Protocol.SetOverheadLimit((int)_overheadLimitBox.Value);
                bool eOk = _mount.Protocol.SetMeridianLimitEastMinutes((int)_meridianEastBox.Value);
                bool wOk = _mount.Protocol.SetMeridianLimitWestMinutes((int)_meridianWestBox.Value);
                DriverSettings.HorizonLimitDeg = (int)_horizonLimitBox.Value;
                DriverSettings.OverheadLimitDeg = (int)_overheadLimitBox.Value;
                DriverSettings.MeridianLimitEastMin = (int)_meridianEastBox.Value;
                DriverSettings.MeridianLimitWestMin = (int)_meridianWestBox.Value;
                if (!hOk || !oOk || !eOk || !wOk)
                {
                    string err = "";
                    try { err = _mount.Protocol.GetLastError(); } catch { }
                    CopyableMessage.Show(this, "Upload limits",
                        "Mount rejected one or more values.\r\n" +
                        "  Horizon:       " + (hOk ? "OK" : "REJECTED") + "\r\n" +
                        "  Overhead:      " + (oOk ? "OK" : "REJECTED") + "\r\n" +
                        "  Meridian east: " + (eOk ? "OK" : "REJECTED") + "\r\n" +
                        "  Meridian west: " + (wOk ? "OK" : "REJECTED") +
                        (string.IsNullOrWhiteSpace(err) ? "" : "\r\n\r\nMount error: " + err));
                    return;
                }
                MessageBox.Show(this, "Limits uploaded to mount.", "Upload limits",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                CopyableMessage.Show(this, "Upload limits", "Upload failed:\r\n\r\n" + ex.ToString());
            }
        }

        private void OnDirPress(string dir)
        {
            if (!_hubConnected) return;
            double rate = (double)_slewSpeedBox.Value;
            _mount.Protocol.SetMoveAxisRateRA(rate);
            _mount.Protocol.SetMoveAxisRateDec(rate);
            if (dir.Contains("N")) _mount.Protocol.MoveNorth();
            if (dir.Contains("S")) _mount.Protocol.MoveSouth();
            if (dir.Contains("E")) _mount.Protocol.MoveEast();
            if (dir.Contains("W")) _mount.Protocol.MoveWest();
        }
        private void OnDirRelease(string dir)
        {
            if (!_hubConnected) return;
            if (dir.Contains("N")) _mount.Protocol.StopNorth();
            if (dir.Contains("S")) _mount.Protocol.StopSouth();
            if (dir.Contains("E")) _mount.Protocol.StopEast();
            if (dir.Contains("W")) _mount.Protocol.StopWest();
        }

        private void Guard(Action a) { try { if (_hubConnected) a(); } catch (Exception ex) { CopyableMessage.Show(this, "Error", ex.ToString()); } }

        // OnStepX returns '0' for rejected park/unpark — surface the mount's own error string
        // so the user sees why (e.g. "no park position saved", "time/site not set", "already
        // unparked") instead of a silent no-op.
        private void ReportIfRejected(string op, bool ok)
        {
            if (ok) return;
            string err = "";
            try { err = _mount.Protocol.GetLastError(); } catch { }
            err = (err ?? "").TrimEnd('#').Trim();
            MessageBox.Show(this,
                op + " rejected by mount." +
                (string.IsNullOrEmpty(err) ? "" : "\r\nMount error: " + err) +
                "\r\n\r\nCheck that date/time and site location are set, and that a park\r\n" +
                "position has been defined (Park → then Unpark).",
                op, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // ---------- Status refresh ----------
        private void RefreshStatus()
        {
            if (!_hubConnected) return;
            var st = _mount.State;
            _raLabel.Text  = CoordFormat.FormatHoursHighPrec(st.RightAscension);
            _decLabel.Text = CoordFormat.FormatDegreesHighPrec(st.Declination);
            _altLabel.Text = st.Altitude.ToString("F2", CultureInfo.InvariantCulture) + "°";
            _azLabel.Text  = st.Azimuth.ToString("F2", CultureInfo.InvariantCulture) + "°";
            _pierLabel.Text = string.IsNullOrEmpty(st.SideOfPier) ? "—" : st.SideOfPier;
            double skyLst = ComputeSkyLstHours(DriverSettings.SiteLongitude);
            double mountLst = st.SiderealTime;
            double dhHours = mountLst - skyLst;
            // Unwrap to nearest-wrap signed minutes.
            while (dhHours > 12) dhHours -= 24;
            while (dhHours < -12) dhHours += 24;
            double deltaMin = dhHours * 60.0;
            _lstLabel.Text = FormatHours(mountLst) + "  /  " + FormatHours(skyLst)
                + "   Δ " + deltaMin.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " min";
            string mode;
            PulseDot.StatusKind stateKind;
            bool pulse = false;
            if (st.AtPark)       { mode = "Parked";   stateKind = PulseDot.StatusKind.Warn; }
            else if (st.Slewing) { mode = "Slewing";  stateKind = PulseDot.StatusKind.Info; }
            else if (st.Tracking){ mode = "Tracking"; stateKind = PulseDot.StatusKind.Ok; pulse = true; }
            else if (st.AtHome)  { mode = "At Home";  stateKind = PulseDot.StatusKind.Info; }
            else                 { mode = "Idle";     stateKind = PulseDot.StatusKind.Neutral; }
            // Slewing badge replaces the state label while the mount is moving.
            bool showBadge = st.Slewing;
            if (_slewingBadge != null)
            {
                if (_slewingBadge.Visible != showBadge) _slewingBadge.Visible = showBadge;
                if (showBadge)
                    _slewingBadge.Coord = CoordFormat.FormatHoursHighPrec(st.RightAscension) + " " + CoordFormat.FormatDegreesHighPrec(st.Declination);
            }
            if (_stateLabel != null)
            {
                _stateLabel.Visible = !showBadge;
                _stateLabel.Text = "State: " + mode;
                _stateLabel.Kind = stateKind;
                _stateLabel.Pulsing = pulse;
            }

            if (_trackingCheck.Checked != st.Tracking)
            {
                _suppressTrackingCheckEvent = true;
                try { _trackingCheck.Checked = st.Tracking; }
                finally { _suppressTrackingCheckEvent = false; }
            }

            // Don't let the poll override a user-initiated mode change for 3 s.
            // If the mount accepted the command its :GT# readback confirms after settling;
            // if it rejected it the UI will revert once the debounce window expires.
            // Also skip while the list is dropped down — writing SelectedIndex under an
            // open popup fights the hover highlight and makes the selection strobe as
            // the mouse moves.
            bool modeDebouncing = (DateTime.UtcNow - _trackingModeSetAt).TotalMilliseconds < 3000;
            if (!modeDebouncing && !_trackingModeBox.DroppedDown && !string.IsNullOrEmpty(st.TrackingMode))
            {
                int idx = _trackingModeBox.Items.IndexOf(st.TrackingMode);
                if (idx >= 0 && idx != _trackingModeBox.SelectedIndex)
                {
                    _suppressTrackingModeEvent = true;
                    try { _trackingModeBox.SelectedIndex = idx; }
                    finally { _suppressTrackingModeEvent = false; }
                }
            }
        }

        private void UpdateClientLabel()
        {
            if (_clientsStatus == null) return;
            int n = ClientRegistry.Count;
            _clientsStatus.Text = "Connected clients: " + n;
            _clientsStatus.Kind = n > 0 ? PulseDot.StatusKind.Ok : PulseDot.StatusKind.Info;
            _clientsStatus.Pulsing = n > 0;
        }

        // ---------- Settings ----------
        private void LoadFromSettings()
        {
            _transportKind.SelectedItem = DriverSettings.TransportKind;
            _portCombo.Text = DriverSettings.SerialPort;
            _baudBox.Value = Math.Max(_baudBox.Minimum, Math.Min(_baudBox.Maximum, DriverSettings.SerialBaud));
            _hostBox.Text = DriverSettings.TcpHost;
            _tcpPortBox.Value = DriverSettings.TcpPort;
            _latBox.Text = CoordFormat.FormatLatitudeDms(DriverSettings.SiteLatitude);
            _lonBox.Text = CoordFormat.FormatLongitudeDms(DriverSettings.SiteLongitude);
            _eleBox.Text = DriverSettings.SiteElevation.ToString("F1", CultureInfo.InvariantCulture);
            _horizonLimitBox.Value = DriverSettings.HorizonLimitDeg;
            _overheadLimitBox.Value = DriverSettings.OverheadLimitDeg;
            _meridianEastBox.Value = Math.Max(_meridianEastBox.Minimum, Math.Min(_meridianEastBox.Maximum, DriverSettings.MeridianLimitEastMin));
            _meridianWestBox.Value = Math.Max(_meridianWestBox.Minimum, Math.Min(_meridianWestBox.Maximum, DriverSettings.MeridianLimitWestMin));
            _syncLimitBox.Value = Math.Max(_syncLimitBox.Minimum, Math.Min(_syncLimitBox.Maximum, DriverSettings.SyncLimitDeg));
            _guideRateBox.Value = (decimal)DriverSettings.GuideRateMultiplier;
            _slewSpeedBox.Value = Math.Max(_slewSpeedBox.Minimum, Math.Min(_slewSpeedBox.Maximum, (decimal)DriverSettings.SlewRateDegPerSec));
            _meridianActionBox.SelectedIndex = DriverSettings.MeridianAutoFlip ? 0 : 1;
        }
        private void SaveSettings()
        {
            DriverSettings.TransportKind = (string)_transportKind.SelectedItem;
            DriverSettings.SerialPort = _portCombo.Text;
            DriverSettings.SerialBaud = (int)_baudBox.Value;
            DriverSettings.TcpHost = _hostBox.Text;
            DriverSettings.TcpPort = (int)_tcpPortBox.Value;
            DriverSettings.GuideRateMultiplier = (double)_guideRateBox.Value;
            DriverSettings.SlewRateDegPerSec = (double)_slewSpeedBox.Value;
            DriverSettings.MeridianAutoFlip = _meridianActionBox.SelectedIndex == 0;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (ClientRegistry.Count > 0)
            {
                var r = MessageBox.Show(this,
                    ClientRegistry.Count + " ASCOM client(s) still connected. Hide to tray instead of closing?",
                    "OnStepX", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) { e.Cancel = true; return; }
                if (r == DialogResult.Yes) { e.Cancel = true; Hide(); return; }
                // "No" path: fall through and terminate the process. Any still-connected
                // clients will get a transport error on their next call — that is the
                // user's explicit intent (close the hub, don't hide).
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            try { _uiTimer.Stop(); } catch { }
            // Always tear down on close — previously this was gated on "no clients",
            // which stranded the process (tray icon present, main window gone, pipe
            // server still serving) when the user chose to close with clients active.
            try { _mount.ForceCloseAll(); } catch { }
            Application.ExitThread();
        }
    }

    // FlowLayoutPanel subclass with auto-scroll-on-focus disabled. The base
    // control calls ScrollControlIntoView whenever a child gains focus (e.g.
    // clicking a button inside a scrolled group), which yanks the visible
    // window away from where the user clicked. Overriding to a no-op keeps
    // manual scroll (wheel, scrollbar) working while preventing the jump.
    internal sealed class NoAutoScrollFlowPanel : FlowLayoutPanel
    {
        protected override System.Drawing.Point ScrollToControl(Control activeControl) => DisplayRectangle.Location;
    }

    // GroupBox with a top-right toggle button that collapses the box to a
    // header-only strip so the user can hide sections they don't care about.
    // Children aren't removed, just hidden by clipping to the reduced Height —
    // re-expand restores exactly what was there, no rebuild. Parent is a
    // FlowLayoutPanel which reflows automatically on SizeChanged.
    internal sealed class CollapsibleGroupBox : GroupBox
    {
        private readonly Button _toggle;
        private int _expandedHeight;
        private const int CollapsedHeight = 22;

        public CollapsibleGroupBox()
        {
            // Distinct button (raised border + darker fill) so it reads as a
            // control rather than blending into the group-box header. Chevron
            // glyph points down when expanded, right when collapsed.
            _toggle = new Button
            {
                Text = "\u25BC",
                Width = 24, Height = 20,
                Top = 0,
                FlatStyle = FlatStyle.Flat,
                TabStop = false,
                UseVisualStyleBackColor = false,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = SystemColors.ControlDark,
                ForeColor = SystemColors.ControlLightLight,
                Font = new Font(System.Drawing.FontFamily.GenericSansSerif, 8f, FontStyle.Bold),
            };
            _toggle.FlatAppearance.BorderSize = 1;
            _toggle.FlatAppearance.BorderColor = SystemColors.ControlDarkDark;
            _toggle.FlatAppearance.MouseOverBackColor = SystemColors.Highlight;
            _toggle.Click += (s, e) => Toggle();
            Controls.Add(_toggle);
            SizeChanged += (s, e) => PositionToggle();
        }

        private void PositionToggle()
        {
            _toggle.Left = Math.Max(0, Width - _toggle.Width - 4);
            _toggle.BringToFront();
        }

        public bool IsCollapsed => Height <= CollapsedHeight + 1;

        public void Toggle()
        {
            if (IsCollapsed) Expand(); else Collapse();
        }

        public void Collapse()
        {
            if (IsCollapsed) return;
            if (Height > CollapsedHeight) _expandedHeight = Height;
            Height = CollapsedHeight;
            _toggle.Text = "\u25B6";
        }

        public void Expand()
        {
            if (!IsCollapsed) return;
            Height = _expandedHeight > CollapsedHeight ? _expandedHeight : 100;
            _toggle.Text = "\u25BC";
        }
    }
}
