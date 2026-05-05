using System;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Diagnostics;
using ASCOM.OnStepX.Hardware.State;

namespace ASCOM.OnStepX.Hardware.Tppa
{
    // Serial bridge that lets NINA's TPPA OAPA plugin auto-discover the hub as
    // a GRBL-speaking polar alignment wedge. Translates the OAPA subset of
    // GRBL into OnStepX focuser-1 / focuser-2 commands (= AXIS4 / AXIS5).
    //
    // Wire protocol (OAPA, 115200 8N1, NewLine = "\n"):
    //   ? \n                                -> <Idle|MPos:x,y,0.000|>\nok\n
    //   $J=G91G21X<val>F<speed>\n           -> ok\n     (relative focuser 1 = Alt)
    //   $J=G91G21Y<val>F<speed>\n           -> ok\n     (relative focuser 2 = Az)
    //   $J=G53X<val>F<speed>\n              -> ok\n     (absolute focuser 1)
    //   $J=G53Y<val>F<speed>\n              -> ok\n     (absolute focuser 2)
    //   XC<mA>\n                            -> ok\n     (Alt run current)
    //   YC<mA>\n                            -> ok\n     (Az run current)
    //   XH<percent>\n                       -> ok\n     (Alt hold percent)
    //   YH<percent>\n                       -> ok\n     (Az hold percent)
    //   ! or 0x18                           -> ok\n     (halt)
    //   anything else                       -> ok\n
    //
    // Position units: raw OnStepX motor steps. User calibrates
    // XGearRatio/YGearRatio in NINA TPPA settings as steps-per-arcminute.
    // Speed F<value>: opaque pass-through, mapped linearly into the OnStepX
    // goto-rate band 5..9. Currents are forwarded to firmware via the
    // hub's per-axis :SXAn,IRUN= / :SXAn,IHOLD= settings, persisted in
    // DriverSettings so they reapply on reconnect.
    internal sealed class TppaSerialBridge : IDisposable
    {
        private readonly MountSession _mount;
        private readonly object _gate = new object();
        private SerialPort _port;
        private CancellationTokenSource _cts;
        private Task _readLoop;
        private string _portName = "";
        private bool _running;

        // Tracks "is a jog currently in progress" — used to populate the
        // GRBL Idle/Run status word. Set briefly while the synchronous move
        // command is being issued; NINA's poll loop then takes over progress
        // tracking via :Fg# reads in BuildStatusReply.
        private volatile bool _moving;

        // Per-axis last-commanded target as the EXACT FLOAT value NINA
        // sent on the wire (e.g. 2.2 for a 0.1-unit nudge × gearRatio=22).
        // BuildStatusReply snaps reported position to this fractional
        // target when the actual integer motor position is within
        // tolerance. Three reasons for fractional precision:
        //   1. Firmware rounds command to integer steps, so motor lands
        //      at floor() of commanded — never matches NINA's float target.
        //   2. NINA's exit tolerance is 0.01 in scaled units; with small
        //      gear ratios any fractional remainder breaks tolerance.
        //   3. OnStepX tends to overshoot :Fr<delta># by 1-3 steps due to
        //      decel ramp + backlash. Across many small nudges this drift
        //      accumulates — tolerance must be wide enough to absorb it.
        // double.NaN = no active target.
        private const int TargetSnapTolerance = 20; // motor steps
        private double _targetX = double.NaN;
        private double _targetY = double.NaN;

        // GRBL motion regex (OAPA subset). G91G21 = relative metric, G53 =
        // absolute. Letter X|Y selects focuser 1|2 (= AXIS4|AXIS5).
        private static readonly Regex JogRelative = new Regex(
            @"^\$J=G91G21\s*(?<ax>[XY])\s*(?<val>-?\d+(\.\d+)?)\s*F\s*(?<spd>\d+(\.\d+)?)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex JogAbsolute = new Regex(
            @"^\$J=G53\s*(?<ax>[XY])\s*(?<val>-?\d+(\.\d+)?)\s*F\s*(?<spd>\d+(\.\d+)?)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // OAPA TMC driver tuning. Run current in mA, hold percent in 0..100.
        private static readonly Regex CurrentCmd = new Regex(
            @"^(?<ax>[XY])(?<kind>[CH])\s*(?<val>\d+(\.\d+)?)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public TppaSerialBridge(MountSession mount) { _mount = mount; }

        public bool IsRunning { get { lock (_gate) return _running; } }
        public string PortName { get { lock (_gate) return _portName; } }

        // Idempotent. Reconciles bridge state to settings + mount-link state:
        //   want = (mount open) && (PA mode) && (port configured)
        //   if want and !running -> start
        //   if !want and running -> stop
        //   if want and running but port changed -> stop+start
        public void Reconcile()
        {
            string desiredPort = "";
            try { desiredPort = (DriverSettings.TppaBridgePort ?? "").Trim(); } catch { }
            bool paMode = false;
            try { paMode = _mount.State?.PolarAlignmentMode ?? false; } catch { }
            bool want = _mount.IsOpen && paMode && !string.IsNullOrEmpty(desiredPort);

            lock (_gate)
            {
                if (!want)
                {
                    if (_running) StopLocked();
                    return;
                }
                if (_running && string.Equals(_portName, desiredPort, StringComparison.OrdinalIgnoreCase)) return;
                if (_running) StopLocked();
                StartLocked(desiredPort);
            }
        }

        private void StartLocked(string portName)
        {
            try
            {
                _port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
                {
                    Handshake = Handshake.None,
                    NewLine = "\n",  // OAPA NewLineSequence
                    ReadTimeout = 200,
                    WriteTimeout = 1000,
                    DtrEnable = true,
                    RtsEnable = true,
                };
                _port.Open();
                _cts = new CancellationTokenSource();
                _portName = portName;
                _running = true;
                _readLoop = Task.Run(() => ReadLoop(_cts.Token));
                DebugLogger.Log("PABRIDGE", "started on " + portName);
            }
            catch (Exception ex)
            {
                DebugLogger.LogException("PABRIDGE", ex);
                try { _port?.Dispose(); } catch { }
                _port = null;
                _running = false;
                _portName = "";
            }
        }

        private void StopLocked()
        {
            try { _cts?.Cancel(); } catch { }
            try { _port?.Close(); } catch { }
            try { _port?.Dispose(); } catch { }
            try { _readLoop?.Wait(500); } catch { }
            _port = null;
            _readLoop = null;
            _cts?.Dispose();
            _cts = null;
            _portName = "";
            _running = false;
            _targetX = double.NaN;
            _targetY = double.NaN;
            DebugLogger.Log("PABRIDGE", "stopped");
        }

        public void Dispose()
        {
            lock (_gate) { if (_running) StopLocked(); }
        }

        private void ReadLoop(CancellationToken ct)
        {
            var buf = new StringBuilder();
            byte[] tmp = new byte[256];
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    int n;
                    try { n = _port.Read(tmp, 0, tmp.Length); }
                    catch (TimeoutException) { continue; }
                    catch (Exception) { break; }
                    for (int i = 0; i < n; i++)
                    {
                        byte b = tmp[i];
                        // GRBL real-time status / halt are single bytes; do
                        // not require a newline.
                        if (b == (byte)'?')
                        {
                            WriteResponse(BuildStatusReply());
                            continue;
                        }
                        if (b == 0x18 || b == (byte)'!')
                        {
                            HandleHalt();
                            WriteResponse("ok\n");
                            continue;
                        }
                        if (b == (byte)'\n' || b == (byte)'\r')
                        {
                            string line = buf.ToString().Trim();
                            buf.Clear();
                            if (line.Length > 0) HandleLine(line);
                            continue;
                        }
                        buf.Append((char)b);
                        if (buf.Length > 256) buf.Clear(); // bail on runaway input
                    }
                }
                catch (Exception ex) { DebugLogger.LogException("PABRIDGE", ex); break; }
            }
        }

        private void HandleLine(string line)
        {
            // GRBL status query also tolerated as a full line.
            if (line == "?") { WriteResponse(BuildStatusReply()); return; }

            // GRBL X axis → focuser 1 (Alt = physical AXIS4),
            // GRBL Y axis → focuser 2 (Az  = physical AXIS5).
            var m = JogRelative.Match(line);
            if (m.Success)
            {
                int focuser = m.Groups["ax"].Value.ToUpperInvariant() == "X" ? 1 : 2;
                double val = double.Parse(m.Groups["val"].Value, CultureInfo.InvariantCulture);
                double spd = double.Parse(m.Groups["spd"].Value, CultureInfo.InvariantCulture);
                IssueMove(focuser, isAbsolute: false, value: val, speed: spd);
                WriteResponse("ok\n");
                return;
            }
            m = JogAbsolute.Match(line);
            if (m.Success)
            {
                int focuser = m.Groups["ax"].Value.ToUpperInvariant() == "X" ? 1 : 2;
                double val = double.Parse(m.Groups["val"].Value, CultureInfo.InvariantCulture);
                double spd = double.Parse(m.Groups["spd"].Value, CultureInfo.InvariantCulture);
                IssueMove(focuser, isAbsolute: true, value: val, speed: spd);
                WriteResponse("ok\n");
                return;
            }
            // OAPA TMC tuning: XC<mA> / YC<mA> = run current; XH<%> / YH<%>
            // = hold percent. Persist + forward to firmware.
            m = CurrentCmd.Match(line);
            if (m.Success)
            {
                int focuser = m.Groups["ax"].Value.ToUpperInvariant() == "X" ? 1 : 2;
                bool isHold = m.Groups["kind"].Value.ToUpperInvariant() == "H";
                int val = (int)Math.Round(double.Parse(m.Groups["val"].Value, CultureInfo.InvariantCulture));
                IssueCurrentSetting(focuser, isHold, val);
                WriteResponse("ok\n");
                return;
            }
            // Unknown but harmless — OAPA doesn't expect strict rejection.
            WriteResponse("ok\n");
        }

        // OAPA current/hold settings. Persisted in DriverSettings for replay
        // on reconnect, then forwarded to firmware via OnStepX axis-setting
        // commands. focuser ∈ {1, 2}, isHold = false for run-current (mA),
        // true for hold-percent (0..100).
        private void IssueCurrentSetting(int focuser, bool isHold, int val)
        {
            int physicalAxis = focuser == 1 ? 4 : 5;
            try
            {
                if (isHold)
                {
                    if (focuser == 1) DriverSettings.PolarAlignAltHoldPercent = val;
                    else              DriverSettings.PolarAlignAzHoldPercent  = val;
                }
                else
                {
                    if (focuser == 1) DriverSettings.PolarAlignAltRunCurrent = val;
                    else              DriverSettings.PolarAlignAzRunCurrent  = val;
                }
            }
            catch (Exception ex) { DebugLogger.LogException("PABRIDGE", ex); }

            // Hand off to the LX200 facade which knows the OnStepX wire format.
            try
            {
                if (isHold) _mount.Protocol.SetAxisHoldPercent(physicalAxis, val);
                else        _mount.Protocol.SetAxisRunCurrentMa(physicalAxis, val);
                DebugLogger.Log("PABRIDGE",
                    "set axis=" + physicalAxis + (isHold ? " hold%=" : " runMa=") + val);
            }
            catch (Exception ex) { DebugLogger.LogException("PABRIDGE", ex); }
        }

        // Synchronous: select axis, set rate, issue move. Returns once the
        // command is on the wire — NINA owns progress tracking via `?` polls.
        // No queue, no MoveWorker — eliminates the 200ms+ latency between
        // command receipt and motor start that was tripping NINA's stuck
        // detector on short moves.
        private void IssueMove(int focuser, bool isAbsolute, double value, double speed)
        {
            var st = _mount.State;
            if (st == null) return;
            int rate = MapSpeedToGotoPreset(speed);
            int steps = (int)Math.Round(value);

            // Compute final target for the snap logic in BuildStatusReply.
            // For RELATIVE moves the baseline is the PREVIOUS reported
            // target, not the actual motor position. Firmware tends to
            // overshoot by 1-3 steps; if we used the actual pos, our
            // tracked target would drift from NINA's expected target by
            // that overshoot every nudge. NINA's view stays anchored to
            // the snapped value we last reported, so we anchor to the
            // same value here.
            int currentPos = focuser == 1 ? st.Axis4PositionSteps : st.Axis5PositionSteps;
            double prevTarget = focuser == 1 ? _targetX : _targetY;
            double baseline = double.IsNaN(prevTarget) ? currentPos : prevTarget;
            double target = isAbsolute ? value : baseline + value;
            if (focuser == 1) _targetX = target; else _targetY = target;

            _moving = true;
            try
            {
                lock (st.PaAxisLock)
                {
                    bool ok = false;
                    try { ok = _mount.Protocol.SetActiveFocuser(focuser); } catch { return; }
                    if (!ok) { DebugLogger.Log("PABRIDGE", ":FA" + focuser + "# rejected"); return; }
                    // Blind variants — see LX200Protocol comment.
                    // SendAndReceive would block 1.5s per command on
                    // firmware that treats these as fire-and-forget.
                    try { _mount.Protocol.SetFocuserRatePresetBlind(rate); } catch { }
                    if (isAbsolute)
                    {
                        try { _mount.Protocol.SetFocuserPositionStepsBlind(steps); } catch { }
                    }
                    else
                    {
                        try { _mount.Protocol.SetFocuserPositionRelativeStepsBlind(steps); } catch { }
                    }
                    DebugLogger.Log("PABRIDGE",
                        "move axis=" + focuser +
                        (isAbsolute ? " abs=" : " rel=") + steps +
                        " rate=:F" + rate + "#");
                }
            }
            finally { _moving = false; }
        }

        // GRBL real-time halt — slams :FQ# on both axes. NINA's poll loop
        // detects motion stop via subsequent `?` reads.
        private void HandleHalt()
        {
            try
            {
                var st = _mount.State;
                if (st == null) return;
                lock (st.PaAxisLock)
                {
                    _mount.Protocol.SetActiveFocuser(1);
                    _mount.Protocol.FocuserHalt();
                    _mount.Protocol.SetActiveFocuser(2);
                    _mount.Protocol.FocuserHalt();
                }
            }
            catch (Exception ex) { DebugLogger.LogException("PABRIDGE", ex); }
        }

        // Linear mapping into goto-rate preset 5..9 with a generous saturation
        // band — TPPA's default F values cluster around 100–500. The user can
        // calibrate the per-iteration step in TPPA settings; choosing F mostly
        // affects observed travel time, not accuracy.
        private static int MapSpeedToGotoPreset(double f)
        {
            if (f <= 50)   return 5; // 0.5×
            if (f <= 200)  return 6; // 0.66×
            if (f <= 500)  return 7; // 1×
            if (f <= 1000) return 8; // 1.5×
            return 9;                 // 2×
        }

        private string BuildStatusReply()
        {
            // Read from cache only. The MountStateCache PA fast poll
            // (200ms cycle when PA mode is active) keeps Axis4/5 position
            // fresh enough for NINA's 300ms motion polling — well inside
            // NINA's 1.5s (5-sample × 300ms) stuck threshold.
            //
            // Live mount reads inside this method had been the culprit:
            // BuildStatusReply ran under PaAxisLock, the hub's PA poll
            // also held the same lock for ~100ms per cycle, so a `?`
            // arriving mid-cycle waited 100-300ms before responding. That
            // variance let NINA's serial input buffer drift out of sync
            // when reply timing crossed its read-timeout boundary, and
            // the next $J would read a stale `<status>` line as the
            // command ack.
            var st = _mount.State;
            double x = st?.Axis4PositionSteps ?? 0;
            double y = st?.Axis5PositionSteps ?? 0;
            bool moving = _moving || (st?.Axis4Moving ?? false) || (st?.Axis5Moving ?? false);

            // Snap to last-commanded target when actual is within tolerance.
            // Firmware rounds the integer step count and may overshoot by
            // 1-2 steps; NINA's exit tolerance is 0.01 in scaled units, so
            // even a single-step delta trips stuck detection. Reporting the
            // exact float target NINA sent makes the wire look like the
            // motor landed precisely.
            if (!moving)
            {
                if (!double.IsNaN(_targetX) && Math.Abs(x - _targetX) <= TargetSnapTolerance) x = _targetX;
                if (!double.IsNaN(_targetY) && Math.Abs(y - _targetY) <= TargetSnapTolerance) y = _targetY;
            }

            string status = moving ? "Run" : "Idle";
            int running = moving ? 1 : 0;
            // OAPA UpdateStatus issues `?` then calls ReadLine TWICE — first
            // reads the status line, second discards a GRBL "ok" ack.
            // Skipping the "ok\n" line makes the second ReadLine block to
            // the 1s scan timeout, breaking port discovery.
            //
            // Format must satisfy OAPA regex:
            //   <(?<status>\w+)\|MPos:x,y,z(?:\|T:t,R:r,E:e,S:s)?\|>
            // Per-axis target/running/endstop/speed group is optional but
            // sending it gives the plugin extra signal during MoveCloser.
            return "<" + status + "|MPos:" +
                   x.ToString("0.000", CultureInfo.InvariantCulture) + "," +
                   y.ToString("0.000", CultureInfo.InvariantCulture) + "," +
                   "0.000|T:0,R:" + running + ",E:0,S:0|>\nok\n";
        }

        private void WriteResponse(string s)
        {
            try
            {
                var p = _port;
                if (p == null || !p.IsOpen) return;
                byte[] bytes = Encoding.ASCII.GetBytes(s);
                p.Write(bytes, 0, bytes.Length);
            }
            catch (Exception ex) { DebugLogger.LogException("PABRIDGE", ex); }
        }

    }
}
