using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using ASCOM.DeviceInterface;
using ASCOM.OnStepX.Hardware;
using ASCOM.OnStepX.Hardware.Transport;

namespace ASCOM.OnStepX.Driver
{
    // In-process ASCOM ITelescopeV3 implementation. Owns a PipeTransport +
    // LX200Protocol pair per client; all reads/writes hop over the named pipe
    // to OnStepX.Hub which owns the mount. No MountSession, no state poller,
    // no reflection — driver is a thin shim and stateless except for target
    // coordinates (ASCOM contract requires setter round-trip) and the COM
    // client's Connected flag.
    [ComVisible(true)]
    [Guid("E3F7B8A1-6C2D-4F3E-9A5B-1F2C3D4E5A6B")]
    [ClassInterface(ClassInterfaceType.None)]
    [ProgId("ASCOM.OnStepX.Telescope")]
    [ServedClassName("OnStepX Telescope Driver")]
    public class Telescope : ITelescopeV3, IDisposable
    {
        private PipeTransport _transport;
        private LX200Protocol _protocol;
        private bool _clientConnected;
        private double _targetRA, _targetDec;
        private bool _targetRaSet, _targetDecSet;

        public Telescope() { }

        // ---------- Connection ----------
        // Connected=true hands off the heavy lifting to HubLauncher: pipe-open,
        // spawn OnStepX.Hub.exe if nobody is listening, retry until the hub's
        // IPC:ISCONNECTED handshake says the mount is live. On timeout throw
        // NotConnectedException with an actionable message — NINA surfaces it
        // verbatim in its "failed to connect" toast.
        public bool Connected
        {
            get => _clientConnected;
            set
            {
                if (value == _clientConnected) return;
                if (value)
                {
                    var t = new PipeTransport();
                    try
                    {
                        if (!HubLauncher.TryEnsureRunning(t, overallTimeoutMs: 10000))
                            throw new TimeoutException(
                                "OnStepX.Hub did not become ready within 10 seconds after auto-launch.");
                    }
                    catch (Exception ex)
                    {
                        try { t.Dispose(); } catch { }
                        throw new ASCOM.NotConnectedException(
                            "OnStepX mount link is not ready. " +
                            "Open OnStepX.Hub and click Connect, then retry.\r\n\r\n" +
                            ex.Message);
                    }
                    _transport = t;
                    _protocol = new LX200Protocol(_transport);
                    // Pop the hub window so the user sees their telescope is live.
                    try { _transport.ShowHub(); } catch { }
                    _clientConnected = true;
                }
                else
                {
                    _clientConnected = false;
                    CloseTransport();
                }
            }
        }

        public string Description => "OnStepX ASCOM Telescope Driver";
        public string DriverInfo => "OnStepX ASCOM driver; LX200-extended over hub pipe; multi-client.";
        // Read from the file-version stamped on the built DLL so release builds
        // with -p:Version=X.Y.Z don't drift from what the driver reports.
        public string DriverVersion
        {
            get
            {
                var asm = typeof(Telescope).Assembly;
                var ver = System.Diagnostics.FileVersionInfo.GetVersionInfo(asm.Location);
                return ver?.FileVersion ?? asm.GetName().Version?.ToString() ?? "0.0.0";
            }
        }
        public short InterfaceVersion => 3;
        public string Name => "OnStepX";
        public ArrayList SupportedActions => new ArrayList(new[] { "GetFirmwareVersion", "GetLastError", "SendRaw" });

        public string Action(string actionName, string actionParameters)
        {
            RequireConnected();
            switch (actionName)
            {
                case "GetFirmwareVersion": return _protocol.GetVersionFull();
                case "GetLastError":       return _protocol.GetLastError();
                // Generic LX200 passthrough — carries whatever raw command the
                // caller hands us, including custom :SX…#/:GX…# variants and
                // future firmware additions the driver does not need to know
                // about. This is the escape hatch for clients that want OnStepX
                // features outside the ITelescopeV3 surface.
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

        // ---------- Capability flags ----------
        public bool CanFindHome => TelescopeCapabilities.CanFindHome;
        public bool CanPark => TelescopeCapabilities.CanPark;
        public bool CanUnpark => TelescopeCapabilities.CanUnpark;
        public bool CanSetPark => TelescopeCapabilities.CanSetPark;
        public bool CanSetTracking => TelescopeCapabilities.CanSetTracking;
        public bool CanSlew => TelescopeCapabilities.CanSlew;
        public bool CanSlewAsync => TelescopeCapabilities.CanSlewAsync;
        public bool CanSlewAltAz => TelescopeCapabilities.CanSlewAltAz;
        public bool CanSlewAltAzAsync => TelescopeCapabilities.CanSlewAltAzAsync;
        public bool CanSync => TelescopeCapabilities.CanSync;
        public bool CanSyncAltAz => TelescopeCapabilities.CanSyncAltAz;
        public bool CanPulseGuide => TelescopeCapabilities.CanPulseGuide;
        public bool CanSetGuideRates => TelescopeCapabilities.CanSetGuideRates;
        public bool CanSetDeclinationRate => TelescopeCapabilities.CanSetDeclinationRate;
        public bool CanSetRightAscensionRate => TelescopeCapabilities.CanSetRightAscensionRate;
        public bool CanSetPierSide => TelescopeCapabilities.CanSetPierSide;
        public bool CanMoveAxis(TelescopeAxes Axis) => TelescopeCapabilities.CanMoveAxis;

        public PierSide DestinationSideOfPier(double RightAscension, double Declination)
        {
            if (!_clientConnected) return PierSide.pierUnknown;
            double ha = SafeHours(() => CoordFormat.ParseHours(_protocol.GetSiderealTime())) - RightAscension;
            while (ha < -12) ha += 24; while (ha > 12) ha -= 24;
            return ha < 0 ? PierSide.pierEast : PierSide.pierWest;
        }

        // ---------- Positions ----------
        // Every read hops over the pipe. Fine at normal ASCOM poll rates
        // (NINA polls RA/Dec ~2 Hz). If a client hammers these and latency
        // matters, the Hub is the right place to add a server-side cache —
        // the driver stays dumb.
        public double RightAscension { get { RequireConnected(); return SafeHours(() => CoordFormat.ParseHours(_protocol.GetRA())); } }
        public double Declination    { get { RequireConnected(); return SafeDegrees(() => _protocol.GetDec()); } }
        public double Altitude       { get { RequireConnected(); return SafeDegrees(() => _protocol.GetAlt()); } }
        public double Azimuth        { get { RequireConnected(); return SafeDegrees(() => _protocol.GetAz()); } }
        public double SiderealTime   { get { RequireConnected(); return SafeHours(() => CoordFormat.ParseHours(_protocol.GetSiderealTime())); } }

        public PierSide SideOfPier
        {
            get
            {
                RequireConnected();
                var reply = TryGet(() => _protocol.GetPierSide()) ?? "";
                foreach (var c in reply.TrimEnd('#'))
                {
                    if (c == 'E' || c == 'e') return PierSide.pierEast;
                    if (c == 'W' || c == 'w') return PierSide.pierWest;
                }
                return PierSide.pierUnknown;
            }
            set => throw new ASCOM.PropertyNotImplementedException("SideOfPier set", true);
        }

        public bool Slewing
        {
            get
            {
                RequireConnected();
                // :GU# status byte conventions (OnStepX, verified against firmware):
                //   'N' = not slewing (absent => slewing)
                //   'I' = park in progress (treat as slewing for ASCOM contract)
                var raw = (TryGet(() => _protocol.GetStatus()) ?? "").TrimEnd('#');
                return raw.IndexOf('N') < 0 || raw.IndexOf('I') >= 0;
            }
        }
        public bool AtPark { get { RequireConnected(); var raw = (TryGet(() => _protocol.GetStatus()) ?? "").TrimEnd('#'); return raw.IndexOf('P') >= 0; } }
        public bool AtHome { get { RequireConnected(); var raw = (TryGet(() => _protocol.GetStatus()) ?? "").TrimEnd('#'); return raw.IndexOf('H') >= 0; } }

        // ---------- Targets ----------
        public double TargetRightAscension
        {
            get { if (!_targetRaSet) throw new ASCOM.ValueNotSetException("TargetRightAscension"); return _targetRA; }
            set { RequireConnected(); if (value < 0 || value >= 24) throw new ASCOM.InvalidValueException("TargetRA"); _protocol.SetTargetRA(value); _targetRA = value; _targetRaSet = true; }
        }
        public double TargetDeclination
        {
            get { if (!_targetDecSet) throw new ASCOM.ValueNotSetException("TargetDeclination"); return _targetDec; }
            set { RequireConnected(); if (value < -90 || value > 90) throw new ASCOM.InvalidValueException("TargetDec"); _protocol.SetTargetDec(value); _targetDec = value; _targetDecSet = true; }
        }

        // ---------- Slew / sync / abort ----------
        public void SlewToCoordinates(double RightAscension, double Declination) { SlewToCoordinatesAsync(RightAscension, Declination); while (Slewing) System.Threading.Thread.Sleep(100); }
        public void SlewToCoordinatesAsync(double RightAscension, double Declination)
        {
            TargetRightAscension = RightAscension;
            TargetDeclination = Declination;
            SlewToTargetAsync();
        }
        public void SlewToTarget() { SlewToTargetAsync(); while (Slewing) System.Threading.Thread.Sleep(100); }
        public void SlewToTargetAsync()
        {
            RequireConnected();
            int rc = _protocol.SlewToTarget();
            if (rc != 0) throw SlewError(rc);
        }
        public void SlewToAltAz(double Azimuth, double Altitude) { SlewToAltAzAsync(Azimuth, Altitude); while (Slewing) System.Threading.Thread.Sleep(100); }
        public void SlewToAltAzAsync(double Azimuth, double Altitude)
        {
            RequireConnected();
            _protocol.SetTargetAlt(Altitude);
            _protocol.SetTargetAz(Azimuth);
            int rc = _protocol.SlewToTargetAltAz();
            if (rc != 0) throw SlewError(rc);
        }
        public void SyncToCoordinates(double RightAscension, double Declination)
        {
            TargetRightAscension = RightAscension;
            TargetDeclination = Declination;
            SyncToTarget();
        }
        public void SyncToTarget()
        {
            RequireConnected();
            if (!_protocol.Sync()) throw new ASCOM.DriverException("Sync failed: " + _protocol.GetLastError());
        }
        public void SyncToAltAz(double Azimuth, double Altitude) => throw new ASCOM.MethodNotImplementedException("SyncToAltAz");

        public void AbortSlew() { RequireConnected(); _protocol.AbortSlew(); }

        // ---------- Move axis ----------
        public void MoveAxis(TelescopeAxes Axis, double Rate)
        {
            RequireConnected();
            if (Axis == TelescopeAxes.axisPrimary)
            {
                if (Rate == 0) { _protocol.StopEast(); _protocol.StopWest(); return; }
                _protocol.SetMoveAxisRateRA(Math.Abs(Rate));
                if (Rate > 0) _protocol.MoveEast(); else _protocol.MoveWest();
            }
            else if (Axis == TelescopeAxes.axisSecondary)
            {
                if (Rate == 0) { _protocol.StopNorth(); _protocol.StopSouth(); return; }
                _protocol.SetMoveAxisRateDec(Math.Abs(Rate));
                if (Rate > 0) _protocol.MoveNorth(); else _protocol.MoveSouth();
            }
            else throw new ASCOM.InvalidValueException("Axis");
        }

        public IAxisRates AxisRates(TelescopeAxes Axis) => new AxisRatesImpl(0.0, 5.0);
        public ITrackingRates TrackingRates => new TrackingRatesImpl();

        // ---------- Pulse guide ----------
        public void PulseGuide(GuideDirections Direction, int Duration)
        {
            RequireConnected();
            char d = 'n';
            switch (Direction)
            {
                case GuideDirections.guideNorth: d = 'n'; break;
                case GuideDirections.guideSouth: d = 's'; break;
                case GuideDirections.guideEast:  d = 'e'; break;
                case GuideDirections.guideWest:  d = 'w'; break;
            }
            _protocol.PulseGuide(d, Duration);
        }
        public bool IsPulseGuiding => false;

        // ---------- Tracking ----------
        public bool Tracking
        {
            get { RequireConnected(); var raw = (TryGet(() => _protocol.GetStatus()) ?? "").TrimEnd('#'); return raw.IndexOf('n') < 0; }
            set { RequireConnected(); if (value) _protocol.TrackingOn(); else _protocol.TrackingOff(); }
        }
        public DriveRates TrackingRate { get => DriveRates.driveSidereal; set { /* OnStepX uses rate Hz; simplified */ } }
        public double RightAscensionRate { get => 0; set => throw new ASCOM.PropertyNotImplementedException("RightAscensionRate", true); }
        public double DeclinationRate { get => 0; set => throw new ASCOM.PropertyNotImplementedException("DeclinationRate", true); }

        public double GuideRateDeclination { get => GuideRateRightAscension; set => GuideRateRightAscension = value; }
        public double GuideRateRightAscension
        {
            get { RequireConnected(); return _protocol.GetGuideRateMultiplier() * 15.041 / 3600.0; }
            set { RequireConnected(); double x = value / (15.041 / 3600.0); _protocol.SetGuideRateMultiplier(x); }
        }

        // ---------- Site ----------
        public double SiteLatitude
        {
            get { RequireConnected(); return CoordFormat.ParseDegrees(_protocol.GetLatitude()); }
            set { RequireConnected(); _protocol.SetLatitude(value); }
        }
        public double SiteLongitude
        {
            get { RequireConnected(); return _protocol.GetLongitudeEastPositive(); }
            set { RequireConnected(); _protocol.SetLongitude(value); }
        }
        public double SiteElevation
        {
            get
            {
                RequireConnected();
                double.TryParse(_protocol.GetElevation().TrimEnd('#'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v);
                return v;
            }
            set { RequireConnected(); _protocol.SetElevation(value); }
        }
        public DateTime UTCDate
        {
            get
            {
                RequireConnected();
                var date = _protocol.GetDate().TrimEnd('#');
                var time = _protocol.GetLocalTime().TrimEnd('#');
                var off  = _protocol.GetUtcOffset().TrimEnd('#');
                if (!DateTime.TryParseExact(date + " " + time, "MM/dd/yy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
                    return DateTime.UtcNow;
                double offH = 0; double.TryParse(off.Replace(":", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out offH);
                return local.AddHours(-offH);
            }
            set
            {
                RequireConnected();
                double offset = (DateTime.Now - DateTime.UtcNow).TotalHours;
                var local = value.AddHours(offset);
                _protocol.SetUtcOffset(offset);
                _protocol.SetLocalDate(local);
                _protocol.SetLocalTime(local);
            }
        }
        public bool DoesRefraction { get => false; set { } }
        public EquatorialCoordinateType EquatorialSystem => EquatorialCoordinateType.equTopocentric;
        public AlignmentModes AlignmentMode => AlignmentModes.algGermanPolar;
        public double ApertureArea => 0;
        public double ApertureDiameter => 0;
        public double FocalLength => 0;
        public short SlewSettleTime { get; set; } = 0;

        // ---------- Park / Home ----------
        public void Park()      { RequireConnected(); _protocol.Park(); }
        public void Unpark()    { RequireConnected(); _protocol.Unpark(); }
        public void SetPark()   { RequireConnected(); _protocol.SetParkHere(); }
        public void FindHome()  { RequireConnected(); _protocol.FindHome(); }

        // ---------- Helpers ----------
        private void RequireConnected()
        {
            if (!_clientConnected || _protocol == null)
                throw new ASCOM.NotConnectedException("OnStepX client not connected");
        }

        private void CloseTransport()
        {
            try { _transport?.Dispose(); } catch { }
            _transport = null;
            _protocol = null;
        }

        // Getter helpers — mount-query failures must never throw back into the
        // ASCOM poll loop or clients spiral into reconnect storms. Return 0 /
        // empty on wire errors; the subsequent poll cycle recovers.
        private static double SafeHours(Func<double> f) { try { return f(); } catch { return 0.0; } }
        private static double SafeDegrees(Func<string> f)
        {
            try
            {
                if (CoordFormat.TryParseDegrees(f(), out var v)) return v;
                return 0.0;
            }
            catch { return 0.0; }
        }
        private static string TryGet(Func<string> f) { try { return f(); } catch { return null; } }

        private Exception SlewError(int rc)
        {
            switch (rc)
            {
                case 1: return new ASCOM.InvalidOperationException("Below horizon limit");
                case 2: return new ASCOM.InvalidOperationException("Above overhead limit");
                case 4: return new ASCOM.ParkedException("Mount parked");
                case 6: return new ASCOM.InvalidOperationException("Outside limits");
                case 7: return new ASCOM.DriverException("Hardware fault");
                case 8: return new ASCOM.InvalidOperationException("Mount already in motion");
                case 9: return new ASCOM.DriverException("Goto failed: " + _protocol.GetLastError());
                default: return new ASCOM.DriverException("Slew failed (code " + rc + ")");
            }
        }
    }

    // These three classes MUST be public + ComVisible so NINA (and anything else
    // using late-bound ASCOM.Com.DriverAccess) can QI for IEnumerable on the
    // __ComObject returned from get_TrackingRates / AxisRates. Internal classes
    // don't get a proper CCW — QueryInterface(IID_IEnumerable) fails with
    // E_NOINTERFACE, which NINA reports as an InvalidCastException during
    // PostConnect → GetTrackingModes. The DispId(-4) on GetEnumerator matches
    // DISPID_NEWENUM so late-bind IDispatch clients can enumerate too.
    [ComVisible(true)]
    [Guid("7C8A1F4B-6D3E-4B8C-9D2E-5F6A7B8C9D0E")]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class AxisRatesImpl : IAxisRates
    {
        private readonly List<IRate> _rates;
        public AxisRatesImpl(double min, double max) { _rates = new List<IRate> { new RateImpl(min, max) }; }
        public int Count => _rates.Count;
        [DispId(-4)]
        public IEnumerator GetEnumerator() => _rates.GetEnumerator();
        public IRate this[int index] => _rates[index - 1];
        public void Dispose() { }
    }

    [ComVisible(true)]
    [Guid("1E4F7A8B-2C5D-4E6F-8A9B-0C1D2E3F4A5B")]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class RateImpl : IRate
    {
        public RateImpl(double min, double max) { Minimum = min; Maximum = max; }
        public double Minimum { get; set; }
        public double Maximum { get; set; }
        public void Dispose() { }
    }

    [ComVisible(true)]
    [Guid("3D6C9B8A-4E7F-5A8B-9C0D-1E2F3A4B5C6D")]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class TrackingRatesImpl : ITrackingRates
    {
        private readonly DriveRates[] _r = { DriveRates.driveSidereal, DriveRates.driveLunar, DriveRates.driveSolar };
        public int Count => _r.Length;
        [DispId(-4)]
        public IEnumerator GetEnumerator() => _r.GetEnumerator();
        public DriveRates this[int index] => _r[index - 1];
        public void Dispose() { }
    }
}
