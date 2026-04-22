using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Hardware;
using ASCOM.OnStepX.Hardware.State;
using ASCOM.OnStepX.Hardware.Transport;

namespace ASCOM.OnStepX.Ui
{
    public sealed class HubForm : Form
    {
        private ComboBox _transportKind, _portCombo;
        private TextBox _hostBox;
        private NumericUpDown _baudBox, _tcpPortBox;
        private Button _autoDetectBtn, _connectBtn, _disconnectBtn;
        private Label _statusLed, _stateLabel, _clientsLabel;
        private TextBox _logBox;
        private CheckBox _logEnable;
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _logPending = new System.Collections.Concurrent.ConcurrentQueue<string>();

        private TextBox _ioBox;
        private CheckBox _ioEnable;
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _ioPending = new System.Collections.Concurrent.ConcurrentQueue<string>();

        private Panel _logPanel;
        private TableLayoutPanel _logGrid;
        private CheckBox _consoleToggle;
        private TextBox _cmdInput;
        private Button _cmdSendBtn;
        private const int ConsoleExpandedHeight = 256;
        private const int ConsoleCollapsedHeight = 40;

        private TextBox _latBox, _lonBox, _eleBox;
        private Button _siteWriteBtn, _siteSyncPcBtn;

        private DateTimePicker _datePicker, _timePicker;
        private NumericUpDown _utcOffsetBox;
        private Button _syncFromPcBtn;

        private CheckBox _trackingCheck;
        private bool _suppressTrackingCheckEvent;
        private ComboBox _trackingModeBox;
        private bool _suppressTrackingModeEvent;
        private DateTime _trackingModeSetAt = DateTime.MinValue;
        private NumericUpDown _guideRateBox, _slewSpeedBox;
        private ComboBox _meridianActionBox;

        private NumericUpDown _horizonLimitBox, _overheadLimitBox;
        private Button _limitsWriteBtn;

        private Label _raLabel, _decLabel, _altLabel, _azLabel, _pierLabel;

        private CheckBox _autoConnectCheck;
        private CheckBox _autoSyncTimeCheck;

        private Button _parkBtn, _unparkBtn, _findHomeBtn, _goHomeBtn, _resetHomeBtn;
        private Button _slewTargetBtn;

        private SlewPadControl _slewPad;

        private readonly Timer _uiTimer = new Timer { Interval = 250 };
        private readonly MountSession _mount = MountSession.Instance;
        private bool _hubConnected;

        public HubForm()
        {
            Text = "OnStepX ASCOM Driver";
            MinimumSize = new Size(1000, 900);
            StartPosition = FormStartPosition.CenterScreen;
            BuildUi();
            LoadFromSettings();

            _uiTimer.Tick += (s, e) => { RefreshStatus(); DrainLog(); DrainIo(); };
            _uiTimer.Start();

            ClientRegistry.Changed += OnClientRegistryChanged;
            _mount.ConnectionChanged += OnMountConnectionChanged;
            TransportLogger.Line += OnTransportLine;
            TransportLogger.Pair += OnTransportPair;
            FormClosed += (s, e) => { TransportLogger.Line -= OnTransportLine; TransportLogger.Pair -= OnTransportPair; };
            UpdateClientLabel();

            Shown += (s, e) => TryAutoConnect();
        }

        private void OnTransportLine(string line)
        {
            if (_logEnable != null && !_logEnable.Checked) return;
            _logPending.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + line);
            // Cap the queue so a disconnected hub can't accumulate forever.
            while (_logPending.Count > 2000 && _logPending.TryDequeue(out _)) { }
        }

        private void OnTransportPair(string cmd, string reply, int elapsedMs)
        {
            if (_ioEnable != null && !_ioEnable.Checked) return;
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " +
                          cmd.PadRight(14) + "  ->  " + reply +
                          (elapsedMs > 0 ? "  (" + elapsedMs + " ms)" : "");
            _ioPending.Enqueue(line);
            while (_ioPending.Count > 2000 && _ioPending.TryDequeue(out _)) { }
        }

        private void DrainLog()
        {
            if (_logBox == null) return;
            if (_logPending.IsEmpty) return;
            var sb = new System.Text.StringBuilder();
            while (_logPending.TryDequeue(out var line)) sb.AppendLine(line);
            if (sb.Length == 0) return;
            // Trim the visible buffer so the control stays responsive after long sessions.
            const int maxChars = 200_000;
            if (_logBox.TextLength + sb.Length > maxChars)
            {
                int keep = Math.Max(0, maxChars - sb.Length);
                _logBox.Text = _logBox.Text.Length > keep ? _logBox.Text.Substring(_logBox.Text.Length - keep) : _logBox.Text;
            }
            _logBox.AppendText(sb.ToString());
        }

        private void DrainIo()
        {
            if (_ioBox == null) return;
            if (_ioPending.IsEmpty) return;
            var sb = new System.Text.StringBuilder();
            while (_ioPending.TryDequeue(out var line)) sb.AppendLine(line);
            if (sb.Length == 0) return;
            const int maxChars = 200_000;
            if (_ioBox.TextLength + sb.Length > maxChars)
            {
                int keep = Math.Max(0, maxChars - sb.Length);
                _ioBox.Text = _ioBox.Text.Length > keep ? _ioBox.Text.Substring(_ioBox.Text.Length - keep) : _ioBox.Text;
            }
            _ioBox.AppendText(sb.ToString());
        }

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
            if (open == _hubConnected) return;
            _hubConnected = open;
            _statusLed.Text = open ? "Connected" : "Disconnected";
            _statusLed.ForeColor = open ? Color.ForestGreen : Color.DarkRed;
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
                    _statusLed.Text = "Connected"; _statusLed.ForeColor = Color.ForestGreen;
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

            var left = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            var right = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
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

        private GroupBox BuildConnectionGroup()
        {
            var g = NewGroup("Connection", 440, 190);
            _transportKind = new ComboBox { Left = 110, Top = 24, Width = 80, DropDownStyle = ComboBoxStyle.DropDownList };
            _transportKind.Items.AddRange(new object[] { "Serial", "TCP" });
            _portCombo = new ComboBox { Left = 110, Top = 58, Width = 100 };
            _portCombo.Items.AddRange(System.IO.Ports.SerialPort.GetPortNames());
            _baudBox = new NumericUpDown { Left = 260, Top = 58, Width = 90, Minimum = 1200, Maximum = 460800, Increment = 1200 };
            _hostBox = new TextBox { Left = 110, Top = 92, Width = 170 };
            _tcpPortBox = new NumericUpDown { Left = 290, Top = 92, Width = 60, Minimum = 1, Maximum = 65535 };
            _autoDetectBtn = new Button { Text = "Auto-Detect", Left = 110, Top = 122, Width = 110 };
            _connectBtn = new Button { Text = "Connect", Left = 228, Top = 122, Width = 90 };
            _disconnectBtn = new Button { Text = "Disconnect", Left = 326, Top = 122, Width = 100 };
            _statusLed = new Label { Text = "Disconnected", Left = 10, Top = 125, Width = 95, ForeColor = Color.DarkRed, Font = new Font(Font, FontStyle.Bold) };
            _autoConnectCheck = new CheckBox {
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
            return g;
        }

        private GroupBox BuildSiteGroup()
        {
            var g = NewGroup("Site", 440, 160);
            _latBox = new TextBox { Left = 110, Top = 26, Width = 220 };
            _lonBox = new TextBox { Left = 110, Top = 54, Width = 220 };
            _eleBox = new TextBox { Left = 110, Top = 82, Width = 120 };
            _siteSyncPcBtn = new Button { Text = "Sync from PC location", Left = 10, Top = 118, Width = 160 };
            _siteWriteBtn  = new Button { Text = "Write to mount",       Left = 180, Top = 118, Width = 160 };
            _siteSyncPcBtn.Click += (s, e) => DoSyncLocationFromPc();
            _siteWriteBtn.Click  += (s, e) => DoWriteSite();
            g.Controls.Add(new Label { Text = "Latitude:", Left = 10, Top = 30, Width = 100 });
            g.Controls.Add(_latBox);
            g.Controls.Add(new Label { Text = "Longitude:", Left = 10, Top = 58, Width = 100 });
            g.Controls.Add(_lonBox);
            g.Controls.Add(new Label { Text = "Elevation (m):", Left = 10, Top = 86, Width = 100 });
            g.Controls.Add(_eleBox);
            g.Controls.Add(_siteSyncPcBtn);
            g.Controls.Add(_siteWriteBtn);
            return g;
        }

        private GroupBox BuildTimeGroup()
        {
            var g = NewGroup("Date / Time", 440, 138);
            _datePicker = new DateTimePicker { Left = 110, Top = 24, Width = 120, Format = DateTimePickerFormat.Short };
            _timePicker = new DateTimePicker { Left = 240, Top = 24, Width = 110, Format = DateTimePickerFormat.Time, ShowUpDown = true };
            _utcOffsetBox = new NumericUpDown { Left = 110, Top = 54, Width = 80, Minimum = -14, Maximum = 14, DecimalPlaces = 1, Increment = 0.5M };
            _syncFromPcBtn = new Button { Text = "Sync from PC", Left = 210, Top = 52, Width = 140 };
            _syncFromPcBtn.Click += (s, e) => DoSyncTime();
            _autoSyncTimeCheck = new CheckBox {
                Text = "Auto-sync date/time from PC on connect", Left = 10, Top = 86, Width = 340,
                Checked = DriverSettings.AutoSyncTimeOnConnect
            };
            _autoSyncTimeCheck.CheckedChanged += (s, e) => DriverSettings.AutoSyncTimeOnConnect = _autoSyncTimeCheck.Checked;
            g.Controls.Add(new Label { Text = "Local:", Left = 10, Top = 28, Width = 100 });
            g.Controls.Add(_datePicker);
            g.Controls.Add(_timePicker);
            g.Controls.Add(new Label { Text = "UTC offset (h):", Left = 10, Top = 58, Width = 100 });
            g.Controls.Add(_utcOffsetBox);
            g.Controls.Add(_syncFromPcBtn);
            g.Controls.Add(_autoSyncTimeCheck);
            return g;
        }

        private GroupBox BuildTrackingGroup()
        {
            var g = NewGroup("Tracking / Slew", 440, 175);
            _trackingCheck = new CheckBox { Text = "Tracking enabled", Left = 10, Top = 26, Width = 160 };
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
            _stateLabel = new Label { Text = "State: —", Left = 180, Top = 28, Width = 170, Font = new Font(Font, FontStyle.Bold) };

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

            _slewSpeedBox = new NumericUpDown { Left = 260, Top = 92, Width = 90, Minimum = 0.1M, Maximum = 10M, DecimalPlaces = 2, Increment = 0.25M };

            _meridianActionBox = new ComboBox { Left = 110, Top = 126, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
            _meridianActionBox.Items.AddRange(new object[] { "Auto Flip", "Stop at Meridian" });
            _meridianActionBox.SelectedIndexChanged += (s, e) => { if (_hubConnected) _mount.Protocol.SetMeridianAutoFlip(_meridianActionBox.SelectedIndex == 0); };

            g.Controls.Add(_trackingCheck);
            g.Controls.Add(_stateLabel);
            g.Controls.Add(new Label { Text = "Tracking rate:", Left = 10, Top = 62, Width = 100 });
            g.Controls.Add(_trackingModeBox);
            g.Controls.Add(new Label { Text = "Guide rate (× sid):", Left = 10, Top = 96, Width = 100 });
            g.Controls.Add(_guideRateBox);
            g.Controls.Add(new Label { Text = "Slew (°/s):", Left = 185, Top = 96, Width = 70 });
            g.Controls.Add(_slewSpeedBox);
            g.Controls.Add(new Label { Text = "At meridian:", Left = 10, Top = 130, Width = 100 });
            g.Controls.Add(_meridianActionBox);
            return g;
        }

        private GroupBox BuildLimitsGroup()
        {
            var g = NewGroup("Limits", 440, 90);
            _horizonLimitBox = new NumericUpDown { Left = 110, Top = 26, Width = 60, Minimum = -30, Maximum = 30 };
            _overheadLimitBox = new NumericUpDown { Left = 260, Top = 26, Width = 60, Minimum = 60, Maximum = 90, Value = 85 };
            _limitsWriteBtn = new Button { Text = "Write limits", Left = 230, Top = 54, Width = 100 };
            _limitsWriteBtn.Click += (s, e) => DoWriteLimits();
            g.Controls.Add(new Label { Text = "Horizon (°):", Left = 10, Top = 30, Width = 100 });
            g.Controls.Add(_horizonLimitBox);
            g.Controls.Add(new Label { Text = "Overhead (°):", Left = 180, Top = 30, Width = 80 });
            g.Controls.Add(_overheadLimitBox);
            g.Controls.Add(_limitsWriteBtn);
            return g;
        }

        private GroupBox BuildPositionGroup()
        {
            var g = NewGroup("Current Position", 360, 150);
            _raLabel = new Label { Left = 110, Top = 28, Width = 230, Text = "—" };
            _decLabel = new Label { Left = 110, Top = 54, Width = 230, Text = "—" };
            _altLabel = new Label { Left = 110, Top = 80, Width = 230, Text = "—" };
            _azLabel = new Label { Left = 110, Top = 106, Width = 230, Text = "—" };
            _pierLabel = new Label { Left = 110, Top = 124, Width = 230, Text = "—" };
            g.Controls.Add(new Label { Text = "RA:", Left = 10, Top = 28, Width = 100 });
            g.Controls.Add(new Label { Text = "Dec:", Left = 10, Top = 54, Width = 100 });
            g.Controls.Add(new Label { Text = "Altitude:", Left = 10, Top = 80, Width = 100 });
            g.Controls.Add(new Label { Text = "Azimuth:", Left = 10, Top = 106, Width = 100 });
            g.Controls.Add(new Label { Text = "Pier side:", Left = 10, Top = 124, Width = 100 });
            g.Controls.Add(_raLabel);
            g.Controls.Add(_decLabel);
            g.Controls.Add(_altLabel);
            g.Controls.Add(_azLabel);
            g.Controls.Add(_pierLabel);
            return g;
        }

        private GroupBox BuildParkHomeGroup()
        {
            var g = NewGroup("Park / Home / Go To", 360, 140);
            _parkBtn = new Button { Text = "Park", Left = 10, Top = 26, Width = 80 };
            _unparkBtn = new Button { Text = "Unpark", Left = 100, Top = 26, Width = 80 };
            _findHomeBtn = new Button { Text = "Search Home", Left = 190, Top = 26, Width = 100 };
            _goHomeBtn = new Button { Text = "Go Home", Left = 10, Top = 60, Width = 110 };
            _resetHomeBtn = new Button { Text = "Reset Home (here)", Left = 130, Top = 60, Width = 160 };
            _slewTargetBtn = new Button { Text = "Slew to Target...", Left = 10, Top = 94, Width = 200 };
            _parkBtn.Click += (s, e) => Guard(() => ReportIfRejected("Park", _mount.Protocol.Park()));
            _unparkBtn.Click += (s, e) => Guard(() => ReportIfRejected("Unpark", _mount.Protocol.Unpark()));
            _findHomeBtn.Click += (s, e) => Guard(() => _mount.Protocol.FindHome());
            _goHomeBtn.Click += (s, e) => Guard(() => _mount.Protocol.GoHome());
            _resetHomeBtn.Click += (s, e) => Guard(() => _mount.Protocol.SetParkHere());
            _slewTargetBtn.Click += (s, e) => OpenSlewTarget();
            g.Controls.Add(_parkBtn); g.Controls.Add(_unparkBtn); g.Controls.Add(_findHomeBtn); g.Controls.Add(_goHomeBtn); g.Controls.Add(_resetHomeBtn);
            g.Controls.Add(_slewTargetBtn);
            return g;
        }

        private void OpenSlewTarget()
        {
            using (var f = new SlewTargetForm(_mount)) f.ShowDialog(this);
        }

        private GroupBox BuildSlewPadGroup()
        {
            var g = NewGroup("Manual Slew", 360, 260);
            _slewPad = new SlewPadControl { Left = 20, Top = 24, Width = 320, Height = 220 };
            _slewPad.DirectionPressed += OnDirPress;
            _slewPad.DirectionReleased += OnDirRelease;
            _slewPad.Stop += () => Guard(() => _mount.Protocol.AbortSlew());
            g.Controls.Add(_slewPad);
            return g;
        }

        private Panel BuildFooter()
        {
            var p = new Panel { Width = 360, Height = 30 };
            _clientsLabel = new Label { Left = 0, Top = 8, Width = 360, Text = "Connected clients: 0", Font = new Font(Font, FontStyle.Bold) };
            p.Controls.Add(_clientsLabel);
            return p;
        }

        private static GroupBox NewGroup(string title, int w, int h)
            => new GroupBox { Text = title, Width = w, Height = h, Margin = new Padding(0, 0, 0, 8) };

        private Panel BuildLogPanel()
        {
            _logPanel = new Panel { Dock = DockStyle.Bottom, Height = ConsoleExpandedHeight, Padding = new Padding(8, 0, 8, 8) };

            _logGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            _logGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            _logGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            _logGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _logGrid.Controls.Add(BuildLogPane(), 0, 0);
            _logGrid.Controls.Add(BuildIoPane(),  1, 0);

            var header = new Panel { Dock = DockStyle.Top, Height = 32 };
            _consoleToggle = new CheckBox { Text = "Show console", Left = 0, Top = 8, Width = 110, Checked = true };
            _consoleToggle.CheckedChanged += (s, e) => ApplyConsoleVisibility();
            var cmdLabel = new Label { Text = "Manual cmd:", Left = 120, Top = 10, Width = 80 };
            _cmdInput = new TextBox { Left = 205, Top = 6, Width = 260, Font = new Font(FontFamily.GenericMonospace, 9f) };
            _cmdInput.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { SendManualCommand(); e.Handled = true; e.SuppressKeyPress = true; }
            };
            _cmdSendBtn = new Button { Text = "Send", Left = 470, Top = 4, Width = 70, Height = 26 };
            _cmdSendBtn.Click += (s, e) => SendManualCommand();
            var hint = new Label { Text = "(e.g. :GVP#  — leading ':' and trailing '#' auto-added)", Left = 550, Top = 10, Width = 350, ForeColor = Color.DimGray };
            header.Controls.Add(_consoleToggle);
            header.Controls.Add(cmdLabel);
            header.Controls.Add(_cmdInput);
            header.Controls.Add(_cmdSendBtn);
            header.Controls.Add(hint);

            _logPanel.Controls.Add(_logGrid);
            _logPanel.Controls.Add(header);
            return _logPanel;
        }

        private void ApplyConsoleVisibility()
        {
            bool show = _consoleToggle.Checked;
            _logGrid.Visible = show;
            _logPanel.Height = show ? ConsoleExpandedHeight : ConsoleCollapsedHeight;
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

        // Raw transport log (every TX/RX line + noise notes).
        private Panel BuildLogPane()
        {
            var pane = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 0) };

            var header = new Panel { Dock = DockStyle.Top, Height = 26 };
            var title = new Label { Text = "Transport log", Left = 0, Top = 4, Width = 120, Font = new Font(Font, FontStyle.Bold) };
            _logEnable = new CheckBox { Text = "Enabled", Left = 130, Top = 4, Width = 80, Checked = true };
            var clearBtn = new Button { Text = "Clear", Left = 220, Top = 0, Width = 70, Height = 24 };
            var copyBtn  = new Button { Text = "Copy",  Left = 295, Top = 0, Width = 70, Height = 24 };
            clearBtn.Click += (s, e) => { _logBox.Clear(); while (_logPending.TryDequeue(out _)) { } };
            copyBtn.Click  += (s, e) => { try { if (!string.IsNullOrEmpty(_logBox.Text)) Clipboard.SetText(_logBox.Text); } catch { } };
            header.Controls.Add(title);
            header.Controls.Add(_logEnable);
            header.Controls.Add(clearBtn);
            header.Controls.Add(copyBtn);

            _logBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = false,
                Font = new Font(FontFamily.GenericMonospace, 8.5f),
                BackColor = Color.Black,
                ForeColor = Color.LightGray
            };
            pane.Controls.Add(_logBox);
            pane.Controls.Add(header);
            return pane;
        }

        // Paired command -> response view, one line per completed exchange.
        private Panel BuildIoPane()
        {
            var pane = new Panel { Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 0) };

            var header = new Panel { Dock = DockStyle.Top, Height = 26 };
            var title = new Label { Text = "Mount I/O (cmd -> reply)", Left = 0, Top = 4, Width = 180, Font = new Font(Font, FontStyle.Bold) };
            _ioEnable = new CheckBox { Text = "Enabled", Left = 190, Top = 4, Width = 80, Checked = true };
            var clearBtn = new Button { Text = "Clear", Left = 280, Top = 0, Width = 70, Height = 24 };
            var copyBtn  = new Button { Text = "Copy",  Left = 355, Top = 0, Width = 70, Height = 24 };
            clearBtn.Click += (s, e) => { _ioBox.Clear(); while (_ioPending.TryDequeue(out _)) { } };
            copyBtn.Click  += (s, e) => { try { if (!string.IsNullOrEmpty(_ioBox.Text)) Clipboard.SetText(_ioBox.Text); } catch { } };
            header.Controls.Add(title);
            header.Controls.Add(_ioEnable);
            header.Controls.Add(clearBtn);
            header.Controls.Add(copyBtn);

            _ioBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = false,
                Font = new Font(FontFamily.GenericMonospace, 8.5f),
                BackColor = Color.Black,
                ForeColor = Color.LightGreen
            };
            pane.Controls.Add(_ioBox);
            pane.Controls.Add(header);
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
            // and responsive while the ESP32 boot-probe loop runs (can take 10+ seconds).
            string kind = (string)_transportKind.SelectedItem;
            string host = _hostBox.Text;
            int tcpPort = (int)_tcpPortBox.Value;
            string port = _portCombo.Text;
            int baud = (int)_baudBox.Value;

            _connectBtn.Enabled = false;
            _autoDetectBtn.Enabled = false;
            _statusLed.Text = "Connecting..."; _statusLed.ForeColor = Color.DarkOrange;

            System.Threading.Tasks.Task.Run(() =>
            {
                Exception err = null;
                double mountMaxSlew = 0.0;
                try
                {
                    ITransport t = kind == "TCP"
                        ? (ITransport)new TcpTransport(host, tcpPort)
                        : new SerialTransport(port, baud);
                    _mount.Configure(t);
                    _mount.Open();
                    // Query mount's configured max slew rate. :GX9A# returns deg/sec;
                    // returns 0 if firmware doesn't support it (pre-OnStepX).
                    try { mountMaxSlew = _mount.Protocol.GetMaxSlewRateDegPerSec(); } catch { }
                }
                catch (Exception ex) { err = ex; }

                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke(new Action(() =>
                    {
                        _connectBtn.Enabled = true;
                        _autoDetectBtn.Enabled = true;
                        if (err != null)
                        {
                            _statusLed.Text = "Disconnected"; _statusLed.ForeColor = Color.DarkRed;
                            CopyableMessage.Show(this, "Connect failed", err.ToString());
                            return;
                        }
                        _hubConnected = true;
                        SaveSettings();

                        // Clamp slew speed box to mount's reported max. If the mount
                        // doesn't expose a max (returns 0), leave the UI limit unchanged
                        // and let the firmware silently clamp the rate at runtime.
                        if (mountMaxSlew > 0.1)
                        {
                            decimal mountMaxDec = (decimal)Math.Round(mountMaxSlew, 2);
                            _slewSpeedBox.Maximum = mountMaxDec;
                            if (_slewSpeedBox.Value > mountMaxDec)
                                _slewSpeedBox.Value = mountMaxDec;
                        }

                        _statusLed.Text = "Connected"; _statusLed.ForeColor = Color.ForestGreen;
                        ReconcileSiteLocationOnConnect();
                        if (DriverSettings.AutoSyncTimeOnConnect)
                        {
                            try { DoSyncTime(); TransportLogger.Note("Auto-synced date/time from PC on connect"); }
                            catch (Exception syncEx) { TransportLogger.Note("Auto-sync time failed: " + syncEx.Message); }
                        }
                    }));
                }
                catch { }
            });
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
                    if (!CoordFormat.TryParseDegrees(lonRaw, out var westPos))
                        throw new FormatException("longitude reply '" + lonRaw + "'");
                    mLon = -westPos;
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
            try { _mount.Close(); } catch { }
            _hubConnected = false;
            _statusLed.Text = "Disconnected"; _statusLed.ForeColor = Color.DarkRed;
        }

        private void DoWriteSite()
        {
            if (!_hubConnected) return;
            double lat = CoordFormat.ParseDegrees(_latBox.Text);
            double lon = CoordFormat.ParseDegrees(_lonBox.Text);
            double ele = double.Parse(_eleBox.Text, CultureInfo.InvariantCulture);
            _mount.Protocol.SetLatitude(lat);
            _mount.Protocol.SetLongitude(lon);
            _mount.Protocol.SetElevation(ele);
            DriverSettings.SiteLatitude = lat; DriverSettings.SiteLongitude = lon; DriverSettings.SiteElevation = ele;
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

        private void DoWriteLimits()
        {
            if (!_hubConnected) return;
            _mount.Protocol.SetHorizonLimit((int)_horizonLimitBox.Value);
            _mount.Protocol.SetOverheadLimit((int)_overheadLimitBox.Value);
            DriverSettings.HorizonLimitDeg = (int)_horizonLimitBox.Value;
            DriverSettings.OverheadLimitDeg = (int)_overheadLimitBox.Value;
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
            string mode;
            if (st.AtPark) mode = "Parked";
            else if (st.Slewing) mode = "Slewing";
            else if (st.Tracking) mode = "Tracking";
            else if (st.AtHome) mode = "At Home";
            else mode = "Idle";
            _stateLabel.Text = "State: " + mode;

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

        private void UpdateClientLabel() { _clientsLabel.Text = "Connected clients: " + ClientRegistry.Count; }

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
            _guideRateBox.Value = (decimal)DriverSettings.GuideRateMultiplier;
            _slewSpeedBox.Value = (decimal)DriverSettings.SlewRateDegPerSec;
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
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            try { _uiTimer.Stop(); } catch { }
            if (ClientRegistry.Count == 0)
            {
                try { _mount.ForceCloseAll(); } catch { }
                Application.ExitThread();
            }
        }
    }
}
