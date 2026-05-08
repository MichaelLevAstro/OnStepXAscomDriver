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
    // OAPA-flavored GRBL serial bridge for NINA TPPA.
    // Maps GRBL X/Y to OnStepX focuser 1/2 (AXIS4/AXIS5).
    internal sealed class TppaSerialBridge : IDisposable
    {
        private readonly MountSession _mount;
        private readonly object _gate = new object();
        private SerialPort _port;
        private CancellationTokenSource _cts;
        private Task _readLoop;
        private string _portName = "";
        private bool _running;
        private volatile bool _moving;

        // Snap reported position to last commanded float target when within
        // tolerance — firmware lands on integer steps so it never matches
        // NINA's fractional target exactly, and decel overshoots by 1-3 steps
        // per nudge. Tolerance must absorb cumulative drift across many
        // small nudges so NINA's 0.01-unit exit check still passes.
        private const int TargetSnapTolerance = 20;
        private double _targetX = double.NaN;
        private double _targetY = double.NaN;

        private static readonly Regex JogRelative = new Regex(
            @"^\$J=G91G21\s*(?<ax>[XY])\s*(?<val>-?\d+(\.\d+)?)\s*F\s*(?<spd>\d+(\.\d+)?)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex JogAbsolute = new Regex(
            @"^\$J=G53\s*(?<ax>[XY])\s*(?<val>-?\d+(\.\d+)?)\s*F\s*(?<spd>\d+(\.\d+)?)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CurrentCmd = new Regex(
            @"^(?<ax>[XY])(?<kind>[CH])\s*(?<val>\d+(\.\d+)?)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public TppaSerialBridge(MountSession mount) { _mount = mount; }

        public bool IsRunning { get { lock (_gate) return _running; } }
        public string PortName { get { lock (_gate) return _portName; } }

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
                        // '?' / 0x18 / '!' are GRBL real-time bytes — no newline.
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
                        if (buf.Length > 256) buf.Clear();
                    }
                }
                catch (Exception ex) { DebugLogger.LogException("PABRIDGE", ex); break; }
            }
        }

        private void HandleLine(string line)
        {
            if (line == "?") { WriteResponse(BuildStatusReply()); return; }

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
            // OAPA tolerates unknown commands — always ack.
            WriteResponse("ok\n");
        }

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

            try
            {
                if (isHold) _mount.Protocol.SetAxisHoldPercent(physicalAxis, val);
                else        _mount.Protocol.SetAxisRunCurrentMa(physicalAxis, val);
                DebugLogger.Log("PABRIDGE",
                    "set axis=" + physicalAxis + (isHold ? " hold%=" : " runMa=") + val);
            }
            catch (Exception ex) { DebugLogger.LogException("PABRIDGE", ex); }
        }

        private void IssueMove(int focuser, bool isAbsolute, double value, double speed)
        {
            var st = _mount.State;
            if (st == null) return;
            int rate = MapSpeedToGotoPreset(speed);
            int steps = (int)Math.Round(value);

            // Anchor relative-move baseline to the PREVIOUS reported target
            // rather than current motor position — firmware overshoots by
            // 1-3 steps per nudge and the drift would compound vs NINA's
            // view if we re-baselined to the actual position each time.
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
                    // Blind variants — :F[n]# / :Fr<sn># are fire-and-forget on
                    // OnStepX, SendAndReceive eats full timeout waiting for a
                    // reply that never comes.
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

        private static int MapSpeedToGotoPreset(double f)
        {
            if (f <= 50)   return 5;
            if (f <= 200)  return 6;
            if (f <= 500)  return 7;
            if (f <= 1000) return 8;
            return 9;
        }

        private string BuildStatusReply()
        {
            // Cache-only read. Live :Fg# under PaAxisLock would race the hub
            // PA fast poll and the resulting reply-timing variance drifts
            // NINA's input buffer out of sync.
            var st = _mount.State;
            double x = st?.Axis4PositionSteps ?? 0;
            double y = st?.Axis5PositionSteps ?? 0;
            bool moving = _moving || (st?.Axis4Moving ?? false) || (st?.Axis5Moving ?? false);

            if (!moving)
            {
                if (!double.IsNaN(_targetX) && Math.Abs(x - _targetX) <= TargetSnapTolerance) x = _targetX;
                if (!double.IsNaN(_targetY) && Math.Abs(y - _targetY) <= TargetSnapTolerance) y = _targetY;
            }

            string status = moving ? "Run" : "Idle";
            int running = moving ? 1 : 0;
            // Trailing "ok\n" is required — OAPA UpdateStatus issues `?` then
            // ReadLine TWICE; without the ack the second read times out and
            // port discovery aborts.
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
