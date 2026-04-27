using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using ASCOM.DeviceInterface;
using ASCOM.OnStepX.Diagnostics;
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
                    DebugLogger.Init("driver");
                    string host = "?";
                    try { host = System.Diagnostics.Process.GetCurrentProcess().ProcessName; } catch { }
                    DebugLogger.Log("CONNECT", "Driver Connected=true requested by host '" + host + "'");
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
                    // Pop the hub window so the user sees their telescope is live.
                    try { _transport.ShowHub(); } catch { }
                    _clientConnected = true;
                    DebugLogger.Log("CONNECT", "Driver connected; pipe up");
                }
                else
                {
                    DebugLogger.Log("CONNECT", "Driver Connected=false");
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

        // Mount truth, not driver truth. Any driver-side HA prediction can
        // disagree with firmware's flip logic near the meridian (LST/RA float
        // jitter at HA≈0), making NINA force a spurious flip that slews into
        // the legs. Echo the mount's current side — the real flip decision
        // happens in firmware on the slew itself (:SX95 auto-flip, :SX96
        // preferred pier, meridian limits).
        public PierSide DestinationSideOfPier(double RightAscension, double Declination)
            => _clientConnected ? SideOfPier : PierSide.pierUnknown;

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
            set
            {
                RequireConnected();
                if (value < 0 || value >= 24) throw new ASCOM.InvalidValueException("TargetRA");
                _protocol.SetTargetRA(value);
                _targetRA = value;
                _targetRaSet = true;
                DebugLogger.Log("TARGET", "set RA=" + FormatHours(value));
            }
        }
        public double TargetDeclination
        {
            get { if (!_targetDecSet) throw new ASCOM.ValueNotSetException("TargetDeclination"); return _targetDec; }
            set
            {
                RequireConnected();
                if (value < -90 || value > 90) throw new ASCOM.InvalidValueException("TargetDec");
                _protocol.SetTargetDec(value);
                _targetDec = value;
                _targetDecSet = true;
                DebugLogger.Log("TARGET", "set Dec=" + FormatDeg(value));
            }
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
            DebugLogger.Log("SLEW", "SlewToTargetAsync entry; tgtRA=" + FormatHours(_targetRA) + " tgtDec=" + FormatDeg(_targetDec));
            EnforceMeridianLimits();
            int rc = _protocol.SlewToTarget();
            DebugLogger.Log("SLEW", "SlewToTarget :MS# rc=" + rc);
            if (rc != 0) { NotifyLimitIfApplicable(rc); throw SlewError(rc); }
        }
        public void SlewToAltAz(double Azimuth, double Altitude) { SlewToAltAzAsync(Azimuth, Altitude); while (Slewing) System.Threading.Thread.Sleep(100); }
        public void SlewToAltAzAsync(double Azimuth, double Altitude)
        {
            RequireConnected();
            DebugLogger.Log("SLEW", "SlewToAltAzAsync az=" + FormatDeg(Azimuth) + " alt=" + FormatDeg(Altitude));
            _protocol.SetTargetAlt(Altitude);
            _protocol.SetTargetAz(Azimuth);
            int rc = _protocol.SlewToTargetAltAz();
            DebugLogger.Log("SLEW", "SlewToTargetAltAz :MA# rc=" + rc);
            if (rc != 0) { NotifyLimitIfApplicable(rc); throw SlewError(rc); }
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
            DebugLogger.Log("SYNC", "SyncToTarget entry; tgtRA=" + FormatHours(_targetRA) + " tgtDec=" + FormatDeg(_targetDec));

            // Capture pier + :GU# status before sync. OnStepX firmware can
            // silently flip its internal pier flag during :CM# when the sync
            // target's HA implies the opposite pier from the one the mount is
            // physically on (e.g. just-after-meridian-flip + plate-solve sync
            // whose target HA is slightly negative). The flag flip leaves
            // firmware reporting W while the mount is still mechanically on E,
            // and the next slew routes through the wrong side. Logging both
            // sides makes the corruption visible in the post-mortem.
            string preGm = "?", preGu = "?";
            try { preGm = (_protocol.GetPierSide() ?? "").TrimEnd('#'); } catch (Exception ex) { preGm = "ERR:" + ex.Message; }
            try { preGu = (_protocol.GetStatus()   ?? "").TrimEnd('#'); } catch (Exception ex) { preGu = "ERR:" + ex.Message; }
            DebugLogger.Log("SYNC", "pre :Gm#='" + preGm + "' :GU#='" + preGu + "'");

            EnforceSyncLimit();
            bool ok = _protocol.Sync();
            if (!ok)
            {
                string err = "?";
                try { err = _protocol.GetLastError(); } catch { }
                DebugLogger.Log("SYNC", "Sync :CM# FAILED err=" + err);
                throw new ASCOM.DriverException("Sync failed: " + err);
            }
            DebugLogger.Log("SYNC", "Sync :CM# OK");

            string postGm = "?", postGu = "?";
            try { postGm = (_protocol.GetPierSide() ?? "").TrimEnd('#'); } catch (Exception ex) { postGm = "ERR:" + ex.Message; }
            try { postGu = (_protocol.GetStatus()   ?? "").TrimEnd('#'); } catch (Exception ex) { postGu = "ERR:" + ex.Message; }
            DebugLogger.Log("SYNC", "post :Gm#='" + postGm + "' :GU#='" + postGu + "'");

            char preP = ExtractPierChar(preGm);
            char postP = ExtractPierChar(postGm);
            if (preP != '?' && postP != '?' && preP != postP)
            {
                DebugLogger.Log("SYNC",
                    "*** PIER FLAG CHANGED across :CM# without physical motion: " + preP + " -> " + postP +
                    ". Next slew will use " + (postP == 'E' ? "East" : "West") +
                    "-pier limits and routing — verify mount mechanical pier matches before slewing.");
            }
        }

        private static char ExtractPierChar(string reply)
        {
            if (string.IsNullOrEmpty(reply)) return '?';
            foreach (var c in reply)
            {
                if (c == 'E' || c == 'e') return 'E';
                if (c == 'W' || c == 'w') return 'W';
            }
            return '?';
        }

        // Driver-side sync distance guardrail. Reads SyncLimitDeg from the same
        // HKCU\Software\ASCOM\OnStepX key the hub writes via DriverSettings.
        // 0 disables. If the angular separation between the mount's current
        // position and the target sync position exceeds the limit, show a
        // confirmation popup in the driver process; Cancel throws so NINA et al.
        // treat the sync as failed rather than silently succeeding.
        private void EnforceSyncLimit()
        {
            int limit = ReadSyncLimitDeg();
            if (limit <= 0) { DebugLogger.Log("SYNC", "EnforceSyncLimit decision=DISABLED limitDeg=0"); return; }
            if (!_targetRaSet || !_targetDecSet)
            {
                DebugLogger.Log("SYNC", "EnforceSyncLimit decision=TARGET_NOT_SET (raSet=" + _targetRaSet + " decSet=" + _targetDecSet + ")");
                return;
            }

            double curRa, curDec;
            try
            {
                curRa  = CoordFormat.ParseHours(_protocol.GetRA());
                curDec = CoordFormat.ParseDegrees(_protocol.GetDec());
            }
            catch (Exception ex)
            {
                // Fail closed. A failed mount-position read can't disambiguate
                // a tiny correction sync from a 90° wrong-pointing sync; better
                // to refuse and surface the wire problem than silently accept.
                DebugLogger.Log("SYNC", "EnforceSyncLimit decision=READ_FAIL (" + ex.Message + ")");
                throw new ASCOM.DriverException(
                    "Sync limit check failed: could not read mount position (" + ex.Message + ")");
            }

            double dist = AngularSeparationDeg(curRa, curDec, _targetRA, _targetDec);

            // Compute target HA + the pier the target's HA implies. OnStepX
            // firmware uses this same heuristic during :CM# to (re)set its
            // internal pier flag, so logging it makes "sync caused phantom
            // pier change" cases visible: if implied pier ≠ current physical
            // pier, the flag is about to flip.
            double lstHours = double.NaN;
            try { lstHours = CoordFormat.ParseHours(_protocol.GetSiderealTime()); } catch { }
            double tgtHaHours = double.IsNaN(lstHours) ? double.NaN : (lstHours - _targetRA);
            while (tgtHaHours > 12) tgtHaHours -= 24;
            while (tgtHaHours < -12) tgtHaHours += 24;
            char impliedPier = double.IsNaN(tgtHaHours) ? '?' : (tgtHaHours > 0 ? 'E' : 'W');
            string curPier = "?";
            try { curPier = (_protocol.GetPierSide() ?? "").TrimEnd('#'); } catch { }

            DebugLogger.Log("SYNC",
                "EnforceSyncLimit curRA=" + FormatHours(curRa) + " curDec=" + FormatDeg(curDec) +
                " tgtRA=" + FormatHours(_targetRA) + " tgtDec=" + FormatDeg(_targetDec) +
                " dist=" + dist.ToString("F3", CultureInfo.InvariantCulture) + "° limit=" + limit + "°" +
                " lst=" + (double.IsNaN(lstHours) ? "?" : FormatHours(lstHours)) +
                " tgtHaHours=" + (double.IsNaN(tgtHaHours) ? "?" : tgtHaHours.ToString("F4", CultureInfo.InvariantCulture)) +
                " impliedPier=" + impliedPier + " curPier='" + curPier + "'");
            if (dist <= limit)
            {
                DebugLogger.Log("SYNC", "EnforceSyncLimit decision=ALLOW");
                return;
            }

            string msg = string.Format(CultureInfo.InvariantCulture,
                "Sync will move the mount by {0:F2}°, which exceeds the configured sync limit of {1}°.\n\n" +
                "A large sync usually means the mount's location or date/time is wrong — " +
                "verify those first. Proceeding with a bad site will slew the wrong way.\n\n" +
                "Continue with the sync?",
                dist, limit);
            var res = System.Windows.Forms.MessageBox.Show(msg,
                "OnStepX sync limit exceeded",
                System.Windows.Forms.MessageBoxButtons.OKCancel,
                System.Windows.Forms.MessageBoxIcon.Warning,
                System.Windows.Forms.MessageBoxDefaultButton.Button2);
            if (res != System.Windows.Forms.DialogResult.OK)
            {
                DebugLogger.Log("SYNC", "EnforceSyncLimit decision=POPUP_CANCEL");
                throw new ASCOM.DriverException(string.Format(CultureInfo.InvariantCulture,
                    "Sync cancelled by user — distance {0:F2}° exceeded configured limit of {1}°.", dist, limit));
            }
            DebugLogger.Log("SYNC", "EnforceSyncLimit decision=POPUP_OK (user confirmed)");
        }

        private static int ReadSyncLimitDeg() => ReadIntSetting("SyncLimitDeg", 0);
        private static int ReadMeridianLimitEastMin() => ReadIntSetting("MeridianLimitEastMin", 15);
        private static int ReadMeridianLimitWestMin() => ReadIntSetting("MeridianLimitWestMin", 15);

        private static int ReadIntSetting(string name, int def)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\ASCOM\OnStepX"))
                {
                    if (k == null) return def;
                    var v = k.GetValue(name);
                    if (v == null) return def;
                    return int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : def;
                }
            }
            catch { return def; }
        }

        // Pre-slew meridian-limit guardrail. NINA's centering slews after a
        // plate-solve sync are the dangerous case: a target HA past the
        // destination-pier limit lets firmware route the slew through the
        // south pole and into the tripod. Mirror EnforceSyncLimit's UX —
        // popup with Continue/Cancel; Cancel throws so NINA reports failure.
        private void EnforceMeridianLimits()
        {
            if (!_targetRaSet) { DebugLogger.Log("SLEW", "EnforceMeridianLimits decision=TARGET_NOT_SET"); return; }

            int eastLimitMin = ReadMeridianLimitEastMin();
            int westLimitMin = ReadMeridianLimitWestMin();
            // Treat both >= 270 (effectively no limit) as disabled. Allow
            // negative values per OnStepX firmware (stop tracking before
            // meridian) — those are the strictest case and must still warn.
            if (eastLimitMin >= 270 && westLimitMin >= 270)
            {
                DebugLogger.Log("SLEW", "EnforceMeridianLimits decision=DISABLED eastMin=" + eastLimitMin + " westMin=" + westLimitMin);
                return;
            }

            double lstHours, curRaHours;
            string pierReply;
            try
            {
                lstHours    = CoordFormat.ParseHours(_protocol.GetSiderealTime());
                curRaHours  = CoordFormat.ParseHours(_protocol.GetRA());
                pierReply   = _protocol.GetPierSide() ?? "";
            }
            catch (Exception ex)
            {
                // Don't block the slew on a transient read failure — firmware
                // still enforces hard limits with rc=6. Best-effort guardrail only.
                DebugLogger.Log("SLEW", "EnforceMeridianLimits decision=READ_FAIL (" + ex.Message + ")");
                return;
            }

            double targetHaHours = lstHours - _targetRA;
            while (targetHaHours > 12) targetHaHours -= 24;
            while (targetHaHours < -12) targetHaHours += 24;
            double targetHaMin = targetHaHours * 60.0;

            // Predict destination pier. Driver intentionally echoes current
            // pier in DestinationSideOfPier (see comment near that property)
            // because firmware owns the flip decision. Approximate the same
            // way: if HA shifts past meridian relative to current pier, treat
            // as a flip; otherwise stay on current side.
            char curPier = '?';
            foreach (var c in pierReply.TrimEnd('#'))
            {
                if (c == 'E' || c == 'e') { curPier = 'E'; break; }
                if (c == 'W' || c == 'w') { curPier = 'W'; break; }
            }

            double curHaHours = lstHours - curRaHours;
            while (curHaHours > 12) curHaHours -= 24;
            while (curHaHours < -12) curHaHours += 24;

            char destPier = curPier;
            if (curPier == 'W' && targetHaHours < 0) destPier = 'E';
            else if (curPier == 'E' && targetHaHours > 0) destPier = 'W';
            // If current pier unknown, fall back to HA sign convention:
            // positive HA (west of meridian) typically lives on E pier.
            if (curPier == '?') destPier = targetHaHours > 0 ? 'E' : 'W';

            int limitMin = destPier == 'E' ? eastLimitMin : westLimitMin;
            DebugLogger.Log("SLEW",
                "EnforceMeridianLimits lst=" + FormatHours(lstHours) +
                " curRA=" + FormatHours(curRaHours) + " curPier=" + curPier +
                " tgtRA=" + FormatHours(_targetRA) +
                " tgtHaMin=" + targetHaMin.ToString("F2", CultureInfo.InvariantCulture) +
                " destPier=" + destPier +
                " eastLimit=" + eastLimitMin + " westLimit=" + westLimitMin +
                " activeLimit=" + limitMin);
            if (targetHaMin <= limitMin)
            {
                DebugLogger.Log("SLEW", "EnforceMeridianLimits decision=ALLOW");
                return;
            }

            string msg = string.Format(CultureInfo.InvariantCulture,
                "Slew target is {0:+0;-0;0} min past meridian on the {1} pier, " +
                "which exceeds the configured {1}-pier meridian limit of {2} min.\n\n" +
                "Continuing may route the mount through the south pole and into " +
                "the tripod. Verify the limits and the target before proceeding.\n\n" +
                "Continue with the slew?",
                targetHaMin, destPier == 'E' ? "East" : "West", limitMin);
            var res = System.Windows.Forms.MessageBox.Show(msg,
                "OnStepX meridian limit",
                System.Windows.Forms.MessageBoxButtons.OKCancel,
                System.Windows.Forms.MessageBoxIcon.Warning,
                System.Windows.Forms.MessageBoxDefaultButton.Button2);
            if (res != System.Windows.Forms.DialogResult.OK)
            {
                DebugLogger.Log("SLEW", "EnforceMeridianLimits decision=POPUP_CANCEL");
                throw new ASCOM.DriverException(string.Format(CultureInfo.InvariantCulture,
                    "Slew cancelled by user — target HA {0:+0;-0;0} min exceeded {1}-pier limit of {2} min.",
                    targetHaMin, destPier == 'E' ? "East" : "West", limitMin));
            }
            DebugLogger.Log("SLEW", "EnforceMeridianLimits decision=POPUP_OK (user confirmed)");
        }

        // Great-circle angular separation. RA in hours, Dec in degrees.
        private static double AngularSeparationDeg(double ra1Hours, double dec1Deg, double ra2Hours, double dec2Deg)
        {
            double ra1 = ra1Hours * Math.PI / 12.0;
            double ra2 = ra2Hours * Math.PI / 12.0;
            double d1 = dec1Deg * Math.PI / 180.0;
            double d2 = dec2Deg * Math.PI / 180.0;
            double cos = Math.Sin(d1) * Math.Sin(d2) + Math.Cos(d1) * Math.Cos(d2) * Math.Cos(ra1 - ra2);
            if (cos > 1) cos = 1; else if (cos < -1) cos = -1;
            return Math.Acos(cos) * 180.0 / Math.PI;
        }
        public void SyncToAltAz(double Azimuth, double Altitude) => throw new ASCOM.MethodNotImplementedException("SyncToAltAz");

        public void AbortSlew()
        {
            RequireConnected();
            DebugLogger.Log("SLEW", "AbortSlew :Q# requested");
            _protocol.AbortSlewVerified();
            DebugLogger.Log("SLEW", "AbortSlew confirmed (mount stopped)");
        }

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
            get
            {
                RequireConnected();
                double v = CoordFormat.ParseDegrees(_protocol.GetLatitude());
                DebugLogger.Log("SITE", "get SiteLatitude=" + FormatDeg(v));
                return v;
            }
            set
            {
                RequireConnected();
                DebugLogger.Log("SITE", "set SiteLatitude=" + FormatDeg(value));
                _protocol.SetLatitude(value);
            }
        }
        // ASCOM east-positive (positive east of Greenwich). LX200Protocol negates
        // at the wire to satisfy OnStepX's Meade west-positive :Sg/:Gg convention.
        public double SiteLongitude
        {
            get
            {
                RequireConnected();
                double v = _protocol.GetLongitude();
                DebugLogger.Log("SITE", "get SiteLongitude=" + FormatDeg(v) + " (east-positive)");
                return v;
            }
            set
            {
                RequireConnected();
                DebugLogger.Log("SITE", "set SiteLongitude=" + FormatDeg(value) + " (east-positive)");
                _protocol.SetLongitude(value);
            }
        }
        public double SiteElevation
        {
            get
            {
                RequireConnected();
                double.TryParse(_protocol.GetElevation().TrimEnd('#'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v);
                DebugLogger.Log("SITE", "get SiteElevation=" + v.ToString("F1", CultureInfo.InvariantCulture) + "m");
                return v;
            }
            set
            {
                RequireConnected();
                DebugLogger.Log("SITE", "set SiteElevation=" + value.ToString("F1", CultureInfo.InvariantCulture) + "m");
                _protocol.SetElevation(value);
            }
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
                {
                    DebugLogger.Log("TIME", "get UTCDate parse FAILED date=" + date + " time=" + time + " off=" + off);
                    return DateTime.UtcNow;
                }
                double offH = 0; double.TryParse(off.Replace(":", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out offH);
                // :GG# reports west-positive (Meade convention). UTC = local + westPos.
                var utc = local.AddHours(offH);
                DebugLogger.Log("TIME", "get UTCDate local=" + local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                    " offWestPos=" + offH.ToString("F2", CultureInfo.InvariantCulture) +
                    " utc=" + utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                return utc;
            }
            set
            {
                RequireConnected();
                double offset = (DateTime.Now - DateTime.UtcNow).TotalHours;
                var local = value.AddHours(offset);
                DebugLogger.Log("TIME", "set UTCDate utc=" + value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                    " offEastPos=" + offset.ToString("F2", CultureInfo.InvariantCulture) +
                    " local=" + local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
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

        // Compact human-readable formatters used only in log output. Wire encoding
        // is owned by CoordFormat / LX200Protocol; these are for the diagnostic file.
        private static string FormatHours(double h)
        {
            if (double.IsNaN(h) || double.IsInfinity(h)) return h.ToString(CultureInfo.InvariantCulture);
            int hh = (int)Math.Floor(h);
            double mTotal = (h - hh) * 60.0;
            int mm = (int)Math.Floor(mTotal);
            double ss = (mTotal - mm) * 60.0;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}h{1:00}m{2:00.00}s", hh, mm, ss);
        }

        private static string FormatDeg(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return d.ToString(CultureInfo.InvariantCulture);
            char sgn = d < 0 ? '-' : '+';
            double a = Math.Abs(d);
            int dd = (int)Math.Floor(a);
            double mTotal = (a - dd) * 60.0;
            int mm = (int)Math.Floor(mTotal);
            double ss = (mTotal - mm) * 60.0;
            return string.Format(CultureInfo.InvariantCulture, "{0}{1:00}d{2:00}m{3:00.0}s", sgn, dd, mm, ss);
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

        // Forward firmware limit-rejection codes to the hub UI's sticky LED.
        // SlewTargetForm raises this internally; for NINA-issued slews the hub
        // process never sees the rc unless we relay it over the IPC pipe.
        private void NotifyLimitIfApplicable(int rc)
        {
            string reason;
            switch (rc)
            {
                case 1: reason = "Below horizon"; break;
                case 2: reason = "Above overhead limit"; break;
                case 6: reason = "Outside meridian limits"; break;
                default:
                    DebugLogger.Log("IPC", "NotifyLimitIfApplicable rc=" + rc + " (no IPC sent)");
                    return;
            }
            DebugLogger.Log("IPC", "NotifyLimit -> hub: rc=" + rc + " reason='" + reason + "'");
            try { _transport?.NotifyLimit(reason); }
            catch (Exception ex) { DebugLogger.LogException("IPC", ex); }
        }

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
