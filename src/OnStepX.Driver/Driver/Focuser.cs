using System;
using System.Collections;
using System.Runtime.InteropServices;
using ASCOM.DeviceInterface;
using ASCOM.OnStepX.Diagnostics;
using ASCOM.OnStepX.Hardware;
using ASCOM.OnStepX.Hardware.Transport;

namespace ASCOM.OnStepX.Driver
{
    // ASCOM IFocuserV2 thin shim. Pipes to OnStepX.Hub which owns the mount
    // and the active focuser selection. The driver always issues bare ":F…#"
    // commands; the firmware-active focuser is owned by the Hub UI dropdown
    // (per :FA[n]#). All position values cross the wire as steps.
    [ComVisible(true)]
    [Guid("9F8B2E5C-3D1A-4F4E-B7C8-2D5E6F7A8B9C")]
    [ClassInterface(ClassInterfaceType.None)]
    [ProgId("ASCOM.OnStepX.Focuser")]
    [ServedClassName("OnStepX Focuser Driver")]
    [AscomDeviceType("Focuser")]
    public class Focuser : IFocuserV2, IDisposable
    {
        private PipeTransport _transport;
        private LX200Protocol _protocol;
        private bool _clientConnected;

        // Cached after first successful read post-connect. Re-read on demand if the
        // mount reports a focuser switch via the Hub UI.
        private int _maxStep;
        private double _stepSize;
        private bool _tempCompAvailable;
        private bool _capsCached;

        public Focuser() { }

        // ---------- Connection ----------
        public bool Connected
        {
            get => _clientConnected;
            set
            {
                if (value == _clientConnected) return;
                if (value)
                {
                    DebugLogger.Init("focuser");
                    string host = "?";
                    try { host = System.Diagnostics.Process.GetCurrentProcess().ProcessName; } catch { }
                    DebugLogger.Log("CONNECT", "Focuser Connected=true requested by host '" + host + "'");
                    var t = new PipeTransport();
                    try
                    {
                        if (!HubLauncher.TryEnsureRunning(t, overallTimeoutMs: 10000))
                            throw new TimeoutException(
                                "OnStepX.Hub did not become ready within 10 seconds after auto-launch.");
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.LogException("CONNECT", ex);
                        try { t.Dispose(); } catch { }
                        throw new ASCOM.NotConnectedException(
                            "OnStepX mount link is not ready. " +
                            "Open OnStepX.Hub and click Connect, then retry.\r\n\r\n" +
                            ex.Message);
                    }
                    _transport = t;
                    _protocol = new LX200Protocol(_transport);
                    try { _transport.ShowHub(); } catch { }
                    _clientConnected = true;
                    _capsCached = false;
                    DebugLogger.Log("CONNECT", "Focuser connected; pipe up");

                    // Surface a clear error if the mount has no focuser axis built —
                    // ASCOM clients calling Move()/Position on a no-focuser board would
                    // otherwise get firmware "0" replies that look like real positions.
                    bool hasFocuser = false;
                    try { hasFocuser = _protocol.HasAnyFocuser(); }
                    catch (Exception ex) { DebugLogger.LogException("FOCUSER", ex); }
                    if (!hasFocuser)
                    {
                        DebugLogger.Log("FOCUSER", ":Fa# reported no focuser configured");
                        _clientConnected = false;
                        CloseTransport();
                        throw new ASCOM.NotConnectedException(
                            "OnStepX board has no focuser axis configured. " +
                            "Enable a focuser in the firmware Config.h (AXIS4_DRIVER_MODEL or higher) and reflash.");
                    }
                }
                else
                {
                    DebugLogger.Log("CONNECT", "Focuser Connected=false");
                    _clientConnected = false;
                    CloseTransport();
                }
            }
        }

        // IFocuserV2 retains Link as a synonym for Connected — alias both ways.
        public bool Link { get => Connected; set => Connected = value; }

        public string Description => "OnStepX ASCOM Focuser Driver";
        public string DriverInfo => "OnStepX ASCOM driver; LX200-extended over hub pipe; multi-client.";
        public string DriverVersion
        {
            get
            {
                var asm = typeof(Focuser).Assembly;
                var ver = System.Diagnostics.FileVersionInfo.GetVersionInfo(asm.Location);
                return ver?.FileVersion ?? asm.GetName().Version?.ToString() ?? "0.0.0";
            }
        }
        public short InterfaceVersion => 2;
        public string Name => "OnStepX Focuser";
        public ArrayList SupportedActions => new ArrayList(new[]
        {
            "GetFocuserStatus",
            "FocuserHome",
            "FocuserGoHome",
            "FocuserZero",
            "GetFirmwareVersion",
            "GetLastError",
            "SendRaw",
        });

        public string Action(string actionName, string actionParameters)
        {
            RequireConnected();
            switch (actionName)
            {
                case "GetFocuserStatus":   return _protocol.GetFocuserStatus();
                case "FocuserHome":        _protocol.FocuserSetHomeHere(); return "OK";
                case "FocuserGoHome":      _protocol.FocuserGoHome();      return "OK";
                case "FocuserZero":        _protocol.FocuserZero();        return "OK";
                case "GetFirmwareVersion": return _protocol.GetVersionFull();
                case "GetLastError":       return _protocol.GetLastError();
                case "SendRaw":            return _transport.SendAndReceive(actionParameters);
                default: throw new ASCOM.ActionNotImplementedException(actionName);
            }
        }

        public void CommandBlind(string command, bool raw = false)
        {
            RequireConnected();
            _transport.SendBlind(raw ? command : ":" + command + "#");
        }
        public bool CommandBool(string command, bool raw = false)
        {
            var r = CommandString(command, raw);
            return !string.IsNullOrEmpty(r) && r[0] == '1';
        }
        public string CommandString(string command, bool raw = false)
        {
            RequireConnected();
            return _transport.SendAndReceive(raw ? command : ":" + command + "#");
        }

        public void Dispose()
        {
            if (_clientConnected) Connected = false;
            else CloseTransport();
        }

        public void SetupDialog()
        {
            using (var f = new SetupDialogForm()) { f.ShowDialog(); }
        }

        // ---------- Capabilities ----------
        public bool Absolute => true;
        public int MaxIncrement { get { EnsureCaps(); return _maxStep; } }
        public int MaxStep      { get { EnsureCaps(); return _maxStep; } }
        public double StepSize  { get { EnsureCaps(); return _stepSize; } }
        public bool TempCompAvailable { get { EnsureCaps(); return _tempCompAvailable; } }

        // ---------- State ----------
        public int Position
        {
            get
            {
                RequireConnected();
                return SafeInt(() => _protocol.GetFocuserPositionSteps());
            }
        }

        public bool IsMoving
        {
            get
            {
                RequireConnected();
                // :FT# -> "M0#" while moving, "S0#" while stopped (digit = rate preset).
                var raw = TryGet(() => _protocol.GetFocuserStatus()) ?? "";
                raw = raw.TrimEnd('#');
                return raw.Length > 0 && (raw[0] == 'M' || raw[0] == 'm');
            }
        }

        public bool TempComp
        {
            get { RequireConnected(); return SafeBool(() => _protocol.GetFocuserTcfEnabled()); }
            set
            {
                RequireConnected();
                if (!TempCompAvailable)
                    throw new ASCOM.PropertyNotImplementedException("TempComp", true);
                _protocol.SetFocuserTcfEnabled(value);
            }
        }

        public double Temperature
        {
            get
            {
                RequireConnected();
                double v = SafeDouble(() => _protocol.GetFocuserTemperatureC());
                if (double.IsNaN(v) || double.IsInfinity(v)) return 0.0;
                return v;
            }
        }

        // ---------- Movement ----------
        public void Move(int Position)
        {
            RequireConnected();
            EnsureCaps();
            if (Position < 0 || Position > _maxStep)
                throw new ASCOM.InvalidValueException("Move",
                    Position.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "0.." + _maxStep.ToString(System.Globalization.CultureInfo.InvariantCulture));
            DebugLogger.Log("FOCUSER", "Move target=" + Position);
            if (!_protocol.SetFocuserPositionSteps(Position))
            {
                string err = "?";
                try { err = _protocol.GetLastError(); } catch { }
                throw new ASCOM.DriverException("Focuser Move rejected: " + err);
            }
        }

        public void Halt()
        {
            RequireConnected();
            DebugLogger.Log("FOCUSER", "Halt :FQ#");
            _protocol.FocuserHalt();
        }

        // ---------- Helpers ----------
        private void RequireConnected()
        {
            if (!_clientConnected || _protocol == null)
                throw new ASCOM.NotConnectedException("OnStepX focuser not connected");
        }

        // Read static capabilities once after connect. Backs MaxStep / StepSize /
        // TempCompAvailable. Re-callable: any wire failure leaves _capsCached
        // false so the next access retries.
        private void EnsureCaps()
        {
            if (_capsCached) return;
            RequireConnected();
            try
            {
                _maxStep  = _protocol.GetFocuserMaxSteps();
                _stepSize = _protocol.GetFocuserMicronsPerStep();
                double t  = _protocol.GetFocuserTemperatureC();
                _tempCompAvailable = !double.IsNaN(t) && !double.IsInfinity(t) && Math.Abs(t) < 1000.0;
                _capsCached = true;
                DebugLogger.Log("FOCUSER",
                    "caps: maxStep=" + _maxStep +
                    " stepSize=" + _stepSize.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                    "um tempCompAvail=" + _tempCompAvailable);
            }
            catch (Exception ex)
            {
                DebugLogger.LogException("FOCUSER", ex);
                // Don't throw — leave caps un-cached and let the caller retry.
            }
        }

        private void CloseTransport()
        {
            try { _transport?.Dispose(); } catch { }
            _transport = null;
            _protocol = null;
            _capsCached = false;
        }

        // Mirror Telescope.cs SafeXxx pattern: a flaky mount poll must not knock
        // an ASCOM client offline. Per project memory feedback_no_throw_mount.
        private static int SafeInt(Func<int> f) { try { return f(); } catch { return 0; } }
        private static bool SafeBool(Func<bool> f) { try { return f(); } catch { return false; } }
        private static double SafeDouble(Func<double> f) { try { return f(); } catch { return 0.0; } }
        private static string TryGet(Func<string> f) { try { return f(); } catch { return null; } }
    }
}
