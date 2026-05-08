using System;
using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using ASCOM.DeviceInterface;
using ASCOM.OnStepX.Diagnostics;
using ASCOM.OnStepX.Hardware;
using ASCOM.OnStepX.Hardware.Transport;
using Microsoft.Win32;

namespace ASCOM.OnStepX.Driver
{
    // ASCOM IRotatorV3 thin shim for OnStepX AXIS3. Pipes to OnStepX.Hub which
    // owns the mount link. Wire angles travel as DMS (sDDD*MM) and span the
    // firmware's signed limit range (typically -180..+180); the IRotatorV3
    // surface speaks 0..360°, so this driver normalizes around the protocol.
    //
    // Reverse semantics: ASCOM Reverse is a queryable boolean — driver tracks
    // it in the registry and applies a sign flip when issuing relative moves /
    // reading position. The firmware ":rR#" toggle is a *separate* concept
    // (derotator direction) and is not bound to ASCOM Reverse here. The Hub
    // UI exposes :rR# under the derotator subgroup explicitly.
    [ComVisible(true)]
    [Guid("B6A2D5F4-7C8E-4B3A-9D1F-3E5C7A8B9D2E")]
    [ClassInterface(ClassInterfaceType.None)]
    [ProgId("ASCOM.OnStepX.Rotator")]
    [ServedClassName("OnStepX Rotator Driver")]
    [AscomDeviceType("Rotator")]
    public class Rotator : IRotatorV3, IDisposable
    {
        private PipeTransport _transport;
        private LX200Protocol _protocol;
        private bool _clientConnected;

        // Cached after connect. Re-read on demand if a wire failure invalidates.
        private double _stepSizeDeg;
        private int    _minDeg;
        private int    _maxDeg;
        private string _capability = "";
        private bool   _capsCached;
        // Firmware-side goto rate is session-only — needs reapply after every
        // mount power cycle. Track once-per-connect so MoveAbsolute / Move can
        // ensure the rate is set before issuing :rS.
        private bool   _gotoRateSet;
        private const int DefaultGotoRatePreset = 7; // 1× base

        // Driver-internal state (NOT firmware-backed). Persisted in registry
        // (HKCU\Software\ASCOM\OnStepX) so multi-client / restart sessions see
        // a stable view of Reverse + Sync. The Hub reads the same keys via
        // DriverSettings — driver and hub share one registry root.
        private bool   _reverse           = RegBool("RotatorReverse", false);
        private double _syncOffsetDeg     = RegDouble("RotatorSyncOffsetDeg", 0.0);
        private float  _lastTargetAscom;

        public Rotator() { }

        // ---------- Connection ----------
        public bool Connected
        {
            get => _clientConnected;
            set
            {
                if (value == _clientConnected) return;
                if (value)
                {
                    DebugLogger.Init("rotator");
                    string host = "?";
                    try { host = System.Diagnostics.Process.GetCurrentProcess().ProcessName; } catch { }
                    DebugLogger.Log("CONNECT", "Rotator Connected=true requested by host '" + host + "'");
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
                    DebugLogger.Log("CONNECT", "Rotator connected; pipe up");

                    // Mirror the Focuser's PA-mode refusal so users don't run
                    // a parallactic-derotation routine while the wedge panel
                    // owns the third column. Rotator (AXIS3) isn't physically
                    // touched by PA mode, but exposing the rotator while
                    // focuser is forcibly disabled would be inconsistent.
                    bool paMode = false;
                    try { paMode = t.IsPolarAlignmentMode(); }
                    catch (Exception ex) { DebugLogger.LogException("ROTATOR", ex); }
                    if (paMode)
                    {
                        DebugLogger.Log("ROTATOR", "hub reports Polar Alignment Wedge mode active — refusing Connect");
                        _clientConnected = false;
                        CloseTransport();
                        throw new ASCOM.NotConnectedException(
                            "OnStepX is in Polar Alignment Wedge mode. " +
                            "Disable Polar Alignment mode in the hub Advanced Settings to use the rotator.");
                    }

                    // Surface a clear error if the mount has no rotator axis built.
                    bool hasRotator = false;
                    try { hasRotator = _protocol.HasRotator(); }
                    catch (Exception ex) { DebugLogger.LogException("ROTATOR", ex); }
                    if (!hasRotator)
                    {
                        DebugLogger.Log("ROTATOR", ":rA# reported no rotator configured");
                        _clientConnected = false;
                        CloseTransport();
                        throw new ASCOM.NotConnectedException(
                            "OnStepX board has no rotator axis configured. " +
                            "Enable AXIS3_DRIVER_MODEL in the firmware Config.h and reflash.");
                    }
                }
                else
                {
                    DebugLogger.Log("CONNECT", "Rotator Connected=false");
                    _clientConnected = false;
                    _gotoRateSet = false;
                    CloseTransport();
                }
            }
        }

        public string Description => "OnStepX ASCOM Rotator Driver";
        public string DriverInfo => "OnStepX ASCOM driver; LX200-extended over hub pipe; multi-client.";
        public string DriverVersion
        {
            get
            {
                var asm = typeof(Rotator).Assembly;
                var ver = System.Diagnostics.FileVersionInfo.GetVersionInfo(asm.Location);
                return ver?.FileVersion ?? asm.GetName().Version?.ToString() ?? "0.0.0";
            }
        }
        public short InterfaceVersion => 3;
        public string Name => "OnStepX Rotator";
        public ArrayList SupportedActions => new ArrayList(new[]
        {
            "GetRotatorStatus",
            "RotatorZero",
            "RotatorSetHalfTravel",
            "RotatorGoHome",
            "DerotatorEnable",
            "DerotatorDisable",
            "DerotatorReverseToggle",
            "RotatorGotoParallactic",
            "GetCapability",
            "GetFirmwareVersion",
            "GetLastError",
            "SendRaw",
        });

        public string Action(string actionName, string actionParameters)
        {
            RequireConnected();
            switch (actionName)
            {
                case "GetRotatorStatus":         return _protocol.GetRotatorStatusRaw();
                case "RotatorZero":              _protocol.RotatorZero();          return "OK";
                case "RotatorSetHalfTravel":     _protocol.RotatorSetHalfTravel(); return "OK";
                case "RotatorGoHome":            _protocol.RotatorGoHome();        return "OK";
                case "DerotatorEnable":          _protocol.EnableDerotator(true);  return "OK";
                case "DerotatorDisable":         _protocol.EnableDerotator(false); return "OK";
                case "DerotatorReverseToggle":   _protocol.RotatorReverseToggle(); return "OK";
                case "RotatorGotoParallactic":   _protocol.RotatorGotoParallactic(); return "OK";
                case "GetCapability":            return _protocol.GetRotatorCapability();
                case "GetFirmwareVersion":       return _protocol.GetVersionFull();
                case "GetLastError":             return _protocol.GetLastError();
                case "SendRaw":                  return _transport.SendAndReceive(actionParameters);
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
        public bool CanReverse => true;
        public float StepSize { get { EnsureCaps(); return (float)_stepSizeDeg; } }

        // ---------- State ----------
        public bool IsMoving
        {
            get
            {
                RequireConnected();
                var raw = TryGet(() => _protocol.GetRotatorStatusRaw()) ?? "";
                return RotatorStatus.Parse(raw).Moving;
            }
        }

        public float Position
        {
            get
            {
                RequireConnected();
                double raw = SafeDouble(() => _protocol.GetRotatorAngleDeg());
                if (double.IsNaN(raw) || double.IsInfinity(raw)) return 0f;
                return (float)ToAscomDeg(raw);
            }
        }

        public float MechanicalPosition
        {
            get
            {
                RequireConnected();
                double raw = SafeDouble(() => _protocol.GetRotatorAngleDeg());
                if (double.IsNaN(raw) || double.IsInfinity(raw)) return 0f;
                return (float)Norm360(raw);
            }
        }

        public float TargetPosition => _lastTargetAscom;

        public bool Reverse
        {
            get => _reverse;
            set
            {
                if (_reverse == value) return;
                _reverse = value;
                RegSet("RotatorReverse", value.ToString());
                DebugLogger.Log("ROTATOR", "Reverse=" + value);
            }
        }

        // ---------- Movement ----------
        public void Halt()
        {
            RequireConnected();
            DebugLogger.Log("ROTATOR", "Halt :rQ#");
            _protocol.RotatorHalt();
        }

        public void Move(float relativePosition)
        {
            RequireConnected();
            ValidateAngle("Move", relativePosition, allowSigned: true);
            // OnStepX firmware ":rr" expects signed 2-digit DMS (max ±90°) and
            // ignores its own parse failures — rather than trip that quirk we
            // read the current angle and issue a normal absolute :rS goto. This
            // also lets relative moves >90° work transparently.
            double rawNow = SafeDouble(() => _protocol.GetRotatorAngleDeg());
            if (double.IsNaN(rawNow))
                throw new ASCOM.DriverException("Rotator Move: cannot read current angle");
            double delta = _reverse ? -relativePosition : relativePosition;
            double mountTarget = ClampToLimits(rawNow + delta);
            EnsureGotoRate();
            DebugLogger.Log("ROTATOR", "Move rel=" + relativePosition.ToString("0.000", CultureInfo.InvariantCulture)
                + " from=" + rawNow.ToString("0.000", CultureInfo.InvariantCulture)
                + " to=" + mountTarget.ToString("0.000", CultureInfo.InvariantCulture));
            if (!_protocol.SetRotatorAngleDeg(mountTarget))
                throw new ASCOM.DriverException("Rotator Move rejected: " + SafeError());
            _lastTargetAscom = NormFloat360(ToAscomDeg(mountTarget));
        }

        public void MoveAbsolute(float ascomDeg)
        {
            RequireConnected();
            ValidateAngle("MoveAbsolute", ascomDeg, allowSigned: false);
            double mountDeg = ToMountDeg(ascomDeg);
            EnsureGotoRate();
            DebugLogger.Log("ROTATOR", "MoveAbsolute ascom=" + ascomDeg.ToString("0.000", CultureInfo.InvariantCulture)
                + " mount=" + mountDeg.ToString("0.000", CultureInfo.InvariantCulture));
            if (!_protocol.SetRotatorAngleDeg(mountDeg))
                throw new ASCOM.DriverException("Rotator MoveAbsolute rejected: " + SafeError());
            _lastTargetAscom = (float)Norm360(ascomDeg);
        }

        public void MoveMechanical(float ascomDeg)
        {
            RequireConnected();
            ValidateAngle("MoveMechanical", ascomDeg, allowSigned: false);
            // Bypass sync offset; Reverse still applies (mechanical-vs-electrical
            // conversion is the same — Reverse is in the cabling, not the sync).
            double mountDeg = ToMountDegMechanical(ascomDeg);
            // No-op suppression: skip a wire round-trip if we're already within
            // one StepSize of the requested mechanical position. ASCOM Conform
            // calls MoveMechanical(MechanicalPosition) to verify a no-op.
            EnsureCaps();
            double current = SafeDouble(() => _protocol.GetRotatorAngleDeg());
            if (!double.IsNaN(current) && _stepSizeDeg > 0
                && Math.Abs(NormSigned(mountDeg - current)) < _stepSizeDeg)
            {
                DebugLogger.Log("ROTATOR", "MoveMechanical no-op (within step size)");
                _lastTargetAscom = (float)Norm360(ascomDeg);
                return;
            }
            EnsureGotoRate();
            DebugLogger.Log("ROTATOR", "MoveMechanical ascom=" + ascomDeg.ToString("0.000", CultureInfo.InvariantCulture)
                + " mount=" + mountDeg.ToString("0.000", CultureInfo.InvariantCulture));
            if (!_protocol.SetRotatorAngleDeg(mountDeg))
                throw new ASCOM.DriverException("Rotator MoveMechanical rejected: " + SafeError());
            _lastTargetAscom = (float)Norm360(ascomDeg);
        }

        // Apply a default goto-rate preset on the firmware once per connection.
        // OnStepX :rS uses settings.gotoRate which is session-state — without
        // an explicit :r5..9# the firmware may still hold a leftover rate from
        // an earlier client (or 0 if never set) and the goto silently no-ops.
        private void EnsureGotoRate()
        {
            if (_gotoRateSet) return;
            try
            {
                _protocol.SetRotatorGotoRatePreset(DefaultGotoRatePreset);
                _gotoRateSet = true;
            }
            catch (Exception ex) { DebugLogger.LogException("ROTATOR", ex); }
        }

        public void Sync(float ascomDeg)
        {
            RequireConnected();
            ValidateAngle("Sync", ascomDeg, allowSigned: false);
            double raw = SafeDouble(() => _protocol.GetRotatorAngleDeg());
            if (double.IsNaN(raw))
                throw new ASCOM.DriverException("Rotator Sync: cannot read current angle");
            double mech = Norm360(raw);
            // Reverse-aware: if the user has set Reverse, they reported a value
            // already in the reversed frame — flip back when computing offset
            // so subsequent reads round-trip cleanly.
            double targetMech = _reverse ? Norm360(-ascomDeg) : Norm360(ascomDeg);
            _syncOffsetDeg = NormSigned(targetMech - mech);
            RegSet("RotatorSyncOffsetDeg", _syncOffsetDeg.ToString("G", CultureInfo.InvariantCulture));
            DebugLogger.Log("ROTATOR", "Sync ascom=" + ascomDeg.ToString("0.000", CultureInfo.InvariantCulture)
                + " offset=" + _syncOffsetDeg.ToString("0.000", CultureInfo.InvariantCulture));
        }

        // ---------- Helpers ----------
        private void RequireConnected()
        {
            if (!_clientConnected || _protocol == null)
                throw new ASCOM.NotConnectedException("OnStepX rotator not connected");
        }

        private void EnsureCaps()
        {
            if (_capsCached) return;
            RequireConnected();
            try
            {
                _stepSizeDeg = _protocol.GetRotatorDegPerStep();
                if (_stepSizeDeg <= 0 || double.IsNaN(_stepSizeDeg) || double.IsInfinity(_stepSizeDeg))
                    _stepSizeDeg = 0.015625; // fall back to firmware default (1/64 deg)
                _minDeg     = _protocol.GetRotatorMinDeg();
                _maxDeg     = _protocol.GetRotatorMaxDeg();
                _capability = _protocol.GetRotatorCapability();
                _capsCached = true;
                DebugLogger.Log("ROTATOR",
                    "caps: stepSize=" + _stepSizeDeg.ToString("0.000000", CultureInfo.InvariantCulture) +
                    "° min=" + _minDeg + "° max=" + _maxDeg + "° cap='" + _capability + "'");
            }
            catch (Exception ex)
            {
                DebugLogger.LogException("ROTATOR", ex);
                // Don't throw — let caller retry on next access.
            }
        }

        private void CloseTransport()
        {
            try { _transport?.Dispose(); } catch { }
            _transport = null;
            _protocol = null;
            _capsCached = false;
        }

        // ASCOM 0..360 -> mount signed degrees (typically -180..+180).
        // Applies sync offset and Reverse. Inverse of ToAscomDeg.
        private double ToMountDeg(double ascomDeg)
        {
            double a = Norm360(ascomDeg);
            if (_reverse) a = Norm360(-a);
            double mech = NormSigned(a - _syncOffsetDeg);
            return ClampToLimits(mech);
        }

        private double ToMountDegMechanical(double ascomDeg)
        {
            double a = Norm360(ascomDeg);
            if (_reverse) a = Norm360(-a);
            return ClampToLimits(NormSigned(a));
        }

        // Mount signed -> ASCOM 0..360. Applies sync offset and Reverse.
        private double ToAscomDeg(double mountDeg)
        {
            double a = mountDeg + _syncOffsetDeg;
            if (_reverse) a = -a;
            return Norm360(a);
        }

        private double ClampToLimits(double mountDeg)
        {
            EnsureCaps();
            // If limits look uninitialized (both 0), skip clamp.
            if (_minDeg == 0 && _maxDeg == 0) return NormSigned(mountDeg);
            // Pick the equivalent angle (mod 360) closest to the limit window.
            double s = NormSigned(mountDeg);
            if (s < _minDeg) s += 360;
            if (s > _maxDeg) s -= 360;
            if (s < _minDeg) s = _minDeg;
            if (s > _maxDeg) s = _maxDeg;
            return s;
        }

        private static double Norm360(double deg)
        {
            double r = deg % 360.0;
            if (r < 0) r += 360.0;
            return r;
        }

        private static double NormSigned(double deg)
        {
            double r = ((deg % 360.0) + 360.0) % 360.0;
            if (r > 180.0) r -= 360.0;
            return r;
        }

        private static float NormFloat360(double deg) => (float)Norm360(deg);

        private void ValidateAngle(string member, float deg, bool allowSigned)
        {
            if (float.IsNaN(deg) || float.IsInfinity(deg))
                throw new ASCOM.InvalidValueException(member, deg.ToString(CultureInfo.InvariantCulture), "finite degrees");
            if (!allowSigned && (deg < 0f || deg >= 360f))
                throw new ASCOM.InvalidValueException(member,
                    deg.ToString(CultureInfo.InvariantCulture), "0 <= position < 360");
        }

        private string SafeError()
        {
            try { return _protocol?.GetLastError() ?? "?"; } catch { return "?"; }
        }

        // Mirror Telescope/Focuser SafeXxx pattern: a flaky mount poll must not
        // knock an ASCOM client offline. Per project memory feedback_no_throw_mount.
        private static double SafeDouble(Func<double> f) { try { return f(); } catch { return double.NaN; } }
        private static string TryGet(Func<string> f) { try { return f(); } catch { return null; } }

        // Registry shim — same root + format as the Hub's DriverSettings so the
        // two halves share one config surface. Driver reads at startup; the
        // setters write through immediately on Reverse / Sync changes.
        private const string RegRoot = @"Software\ASCOM\OnStepX";
        private static string RegGet(string name)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RegRoot))
                    return k?.GetValue(name) as string;
            }
            catch { return null; }
        }
        private static void RegSet(string name, string value)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RegRoot))
                    k?.SetValue(name, value ?? "");
            }
            catch { }
        }
        private static bool RegBool(string name, bool def)
        {
            var s = RegGet(name);
            return bool.TryParse(s, out var v) ? v : def;
        }
        private static double RegDouble(string name, double def)
        {
            var s = RegGet(name);
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
        }
    }
}
