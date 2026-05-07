using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Diagnostics;

namespace ASCOM.OnStepX.Hardware.State
{
    internal sealed class MountStateCache : IDisposable
    {
        public double RightAscension;   // hours
        public double Declination;      // deg
        public double Altitude;         // deg
        public double Azimuth;          // deg
        public double SiderealTime;     // hours
        public bool Tracking;
        public bool Slewing;
        public bool AtPark;
        public bool AtHome;
        public bool AutoMeridianFlip;   // parsed from :GU# 'a' char (matches OnStep web view)
        public string SideOfPier;       // "E" or "W" or ""
        public string TrackingMode;     // "Sidereal" | "Solar" | "Lunar" | "King" | ""
        public string LastStatusString; // raw :GU# reply
        public DateTime LastUpdateUtc;
        // Raw mechanical axis angles in degrees from :GX42#/:GX43#. Independent
        // of LST and pier-side mapping — gives the unambiguous physical
        // position of the axes. Axis 1 (RA): 0° at meridian, +west, -east on
        // a GEM with default OnStep config; ±90° is the horizon. NaN when the
        // firmware doesn't expose the command (older non-X builds).
        public double Axis1Deg = double.NaN;
        public double Axis2Deg = double.NaN;

        // Focuser snapshot. FocuserAvailable + FocuserCount are probed once at
        // connect (see TryProbeFocuser); the per-tick fields are refreshed on a
        // 4× slower cadence than the main mount poll because focuser state
        // changes are infrequent and an extra ~3 round-trips per cycle would
        // bite into UI responsiveness.
        public bool FocuserAvailable;
        public int  FocuserCount;        // 0..6 detected at connect
        public int  FocuserActiveIndex;  // last known firmware-active focuser
        public int  FocuserPosition;     // steps
        public bool FocuserMoving;
        public double FocuserTempC = double.NaN;

        // Rotator snapshot. Probed once at connect; per-tick fields (angle,
        // moving, derot state) ride along on the same 4× slow cadence as the
        // focuser fields. Only one rotator on AXIS3 (no equivalent of focuser
        // 1..6 walk). Capability is "D" derotate-capable, "R" rotate-only,
        // or "" / "N" if firmware doesn't expose :GX98#.
        public bool   RotatorAvailable;
        public string RotatorCapability = "";
        public double RotatorAngleDeg = double.NaN;
        public bool   RotatorMoving;
        public bool   RotatorDerotating;
        public bool   RotatorDerotReversed;
        public int    RotatorRatePreset;
        public int    RotatorMinDeg;
        public int    RotatorMaxDeg;
        public double RotatorStepSizeDeg;
        public int    RotatorBacklashSteps;

        // Polar Alignment Wedge mode. Resolved during Start() after the
        // focuser/rotator probes. When true, FocuserAvailable + RotatorAvailable
        // are forced false so the existing VMs stay dormant; the polar
        // alignment poll path takes over the focuser ride-along slot.
        //
        // Wire numbering: OnStepX ":FA[n]#" uses *focuser index* (1..count),
        // not physical axis number. AXIS4 + AXIS5 enabled in Config.h →
        // focuser 1 (Alt) + focuser 2 (Az). The fields below cache the
        // per-focuser position — naming preserves the physical axis number
        // for clarity at the readout site.
        public bool PolarAlignmentMode;
        public int  Axis4PositionSteps;   // focuser 1 = physical AXIS4 = Alt
        public int  Axis5PositionSteps;   // focuser 2 = physical AXIS5 = Az
        public bool Axis4Moving;
        public bool Axis5Moving;

        // Race lock for axis-switch sequences. The :FA[n]# selector is global —
        // any thread that issues :FA[n]# followed by a command that depends on
        // the active focuser (:Fg#, :Fr#, :F+#, etc.) must hold this lock for
        // the entire pair. Otherwise the poll-loop's :FA4#/:FA5# sandwich can
        // interleave with a user-click sequence and the click ends up acting
        // on the wrong axis.
        public readonly object PaAxisLock = new object();

        public event EventHandler Updated;

        private readonly LX200Protocol _p;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task _pollTask;
        private Task _paFastPollTask;
        private int _pollMs;
        private const int PaFastPollMs = 200;  // 200ms PA-mode cadence — keeps NINA TPPA's 300ms `?` polls always seeing fresh position
        private int _focuserPollTick; // counter for slow-cadence focuser ride-along

        // Lazy re-probe budget. On a cold mount boot the focuser axis often
        // isn't ready when MountSession declares the mount responsive (:GVP#
        // returned), so the initial :Fa# can come back empty even though
        // the axis is enabled in firmware. We retry from inside the poll loop
        // until either we find a focuser or burn through this counter.
        private int _focuserLateProbeAttempts;
        private const int FocuserLateProbeMaxAttempts = 30; // ~30 cycles × 750 ms ≈ 22 s

        // Rotator parallel: same cold-boot late-probe budget. AXIS3 sometimes
        // initializes after MountSession declares the mount responsive.
        private int _rotatorPollTick;
        private int _rotatorLateProbeAttempts;
        private const int RotatorLateProbeMaxAttempts = 30;

        // 750ms is a good middle ground: each poll cycle issues ~7 serial round-trips
        // (~200ms total), so tighter intervals starve UI-thread commands of lock time
        // and make the hub feel sluggish when the user clicks a button.
        public MountStateCache(LX200Protocol p, int pollIntervalMs = 750)
        {
            _p = p;
            _pollMs = pollIntervalMs;
        }

        public int PollIntervalMs
        {
            get => _pollMs;
            set => _pollMs = Math.Max(50, value);
        }

        public void Start()
        {
            if (_pollTask != null) return;
            RunInitialFocuserProbe();
            RunInitialRotatorProbe();
            ResolvePolarAlignmentMode();
            _pollTask = Task.Run(() => PollLoop(_cts.Token));
            // PA mode runs a separate fast cache-refresh task so position
            // tracking doesn't have to share cadence with the heavy mount
            // RA/Dec/Alt/Az poll. NINA TPPA polls `?` every 300ms — 200ms
            // here guarantees fresh values without compounding lock time.
            if (PolarAlignmentMode)
                _paFastPollTask = Task.Run(() => PaFastPollLoop(_cts.Token));
        }

        // PA mode is true when:
        //   1) user has flipped DriverSettings.PolarAlignmentMode in Advanced Settings, AND
        //   2) firmware exposes ≥2 focuser axes (axis 4 + axis 5 enabled in Config.h).
        // Forces FocuserAvailable + RotatorAvailable false so existing VMs stay
        // dormant — single source of truth for "is this a wedge or a focuser".
        private void ResolvePolarAlignmentMode()
        {
            bool requested = false;
            try { requested = DriverSettings.PolarAlignmentMode; } catch { }
            PolarAlignmentMode = requested && FocuserAvailable && FocuserCount >= 2;
            if (PolarAlignmentMode)
            {
                DebugLogger.Log("PA",
                    "Polar Alignment Wedge mode active — focuser/rotator panels disabled. " +
                    "axis4+axis5 will drive Alt/Az.");
                FocuserAvailable = false;
                RotatorAvailable = false;
                _focuserLateProbeAttempts = 0;
                _rotatorLateProbeAttempts = 0;
            }
        }

        // Probe how many focusers are configured by walking :FA[1..6]#. The
        // last index that accepts the select is the count. Restores the prior
        // active index. Returns true on a successful detection (focuser
        // present), false when :Fa# reported zero or the wire didn't answer.
        private bool TryProbeFocuser()
        {
            try
            {
                if (!_p.HasAnyFocuser())
                {
                    FocuserAvailable = false;
                    FocuserCount = 0;
                    return false;
                }
                int prior = 1;
                try { prior = _p.GetActiveFocuser(); if (prior < 1 || prior > 6) prior = 1; } catch { }
                int found = 0;
                for (int i = 1; i <= 6; i++)
                {
                    bool ok = false;
                    try { ok = _p.SetActiveFocuser(i); } catch { }
                    if (ok) found = i;
                    else break; // OnStepX rejects the first absent index — stop probing.
                }
                if (found < 1) found = 1; // :Fa# said yes; trust at least 1.
                FocuserAvailable = true;
                FocuserCount = found;
                try { _p.SetActiveFocuser(prior >= 1 && prior <= found ? prior : 1); } catch { }
                FocuserActiveIndex = prior >= 1 && prior <= found ? prior : 1;
                DebugLogger.Log("FOCUSER", "probe found=" + found + " restored active=" + FocuserActiveIndex);
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.LogException("FOCUSER", ex);
                FocuserAvailable = false;
                FocuserCount = 0;
                return false;
            }
        }

        // Initial-connect probe: try a few times back-to-back with short gaps
        // to catch the common case where :Fa# answers empty for the first one
        // or two queries after the firmware comes online. Failures here arm
        // the lazy re-probe in the poll loop.
        private void RunInitialFocuserProbe()
        {
            const int initialAttempts = 6;
            for (int i = 0; i < initialAttempts; i++)
            {
                if (TryProbeFocuser()) { _focuserLateProbeAttempts = 0; return; }
                try { Thread.Sleep(500); } catch { }
            }
            _focuserLateProbeAttempts = FocuserLateProbeMaxAttempts;
            DebugLogger.Log("FOCUSER", "initial probe empty after " + initialAttempts +
                            " attempts; lazy retry armed (" + FocuserLateProbeMaxAttempts + " cycles)");
        }

        // Rotator analogue of TryProbeFocuser. Probes :rA# for presence then
        // :GX98# / :rD# / :rI# / :rM# / :rb# to fill the static capability
        // fields. No active-index walk because OnStepX exposes a single
        // rotator on AXIS3.
        private bool TryProbeRotator()
        {
            try
            {
                if (!_p.HasRotator())
                {
                    RotatorAvailable = false;
                    return false;
                }
                RotatorAvailable = true;
                try { RotatorCapability     = _p.GetRotatorCapability(); }   catch { RotatorCapability = ""; }
                try { RotatorStepSizeDeg    = _p.GetRotatorDegPerStep(); }   catch { }
                try { RotatorMinDeg         = _p.GetRotatorMinDeg(); }       catch { }
                try { RotatorMaxDeg         = _p.GetRotatorMaxDeg(); }       catch { }
                try { RotatorBacklashSteps  = _p.GetRotatorBacklashSteps(); } catch { }
                DebugLogger.Log("ROTATOR",
                    "probe cap='" + RotatorCapability + "' step=" +
                    RotatorStepSizeDeg.ToString("0.000000", CultureInfo.InvariantCulture) +
                    "° lim=[" + RotatorMinDeg + ".." + RotatorMaxDeg + "]°");
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.LogException("ROTATOR", ex);
                RotatorAvailable = false;
                return false;
            }
        }

        private void RunInitialRotatorProbe()
        {
            const int initialAttempts = 6;
            for (int i = 0; i < initialAttempts; i++)
            {
                if (TryProbeRotator()) { _rotatorLateProbeAttempts = 0; return; }
                try { Thread.Sleep(500); } catch { }
            }
            _rotatorLateProbeAttempts = RotatorLateProbeMaxAttempts;
            DebugLogger.Log("ROTATOR", "initial probe empty after " + initialAttempts +
                            " attempts; lazy retry armed (" + RotatorLateProbeMaxAttempts + " cycles)");
        }

        public void Stop()
        {
            _cts.Cancel();
            try { _pollTask?.Wait(1000); } catch { }
            try { _paFastPollTask?.Wait(1000); } catch { }
            _pollTask = null;
            _paFastPollTask = null;
        }

        // Live re-resolve of PA mode without dropping the mount link. Called
        // when the user flips the Advanced "Enable Automatic Polar Alignment"
        // toggle. Three transitions to handle:
        //   1) PA off -> PA on  : resolve again (uses existing focuser probe
        //                         results), spin up the fast poll task,
        //                         FocuserAvailable + RotatorAvailable get
        //                         forced false inside ResolvePolarAlignmentMode.
        //   2) PA on  -> PA off : re-probe focuser + rotator so their VMs
        //                         come back online, then resolve again.
        //                         Stop the fast poll task.
        //   3) Setting unchanged: no-op.
        // Bridge reconcile is the caller's responsibility (MainViewModel
        // chains it after this call).
        public void RefreshPolarAlignmentMode()
        {
            if (_pollTask == null) return; // not connected; Start() will pick it up
            bool wasInPaMode = PolarAlignmentMode;
            bool requested = false;
            try { requested = DriverSettings.PolarAlignmentMode; } catch { }

            if (wasInPaMode && !requested)
            {
                // Restore focuser + rotator availability from firmware before
                // re-resolving — otherwise FocuserAvailable stays false and
                // those VMs would still report unavailable.
                try { TryProbeFocuser(); }  catch (Exception ex) { DebugLogger.LogException("PA", ex); }
                try { TryProbeRotator(); }  catch (Exception ex) { DebugLogger.LogException("PA", ex); }
            }

            ResolvePolarAlignmentMode();

            if (PolarAlignmentMode && _paFastPollTask == null)
            {
                _paFastPollTask = Task.Run(() => PaFastPollLoop(_cts.Token));
                DebugLogger.Log("PA", "live refresh: fast poll task started");
            }
            else if (!PolarAlignmentMode && _paFastPollTask != null)
            {
                // Fast poll task exits naturally when PolarAlignmentMode flips
                // false (it checks the flag each tick). Drop the handle so a
                // future toggle-on starts a fresh task.
                _paFastPollTask = null;
                DebugLogger.Log("PA", "live refresh: fast poll task release");
            }
            DebugLogger.Log("PA",
                "live refresh: requested=" + requested +
                " resolved=" + PolarAlignmentMode +
                " focuserAvail=" + FocuserAvailable +
                " rotatorAvail=" + RotatorAvailable);
        }

        public void Dispose() { Stop(); _cts.Dispose(); }

        private void PollLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var raS = _p.GetRA();
                    var decS = _p.GetDec();
                    var altS = _p.GetAlt();
                    var azS  = _p.GetAz();
                    var stS  = _p.GetSiderealTime();
                    var psS  = _p.GetPierSide();
                    var gus  = _p.GetStatus();
                    double rateHz = 0.0;
                    try { rateHz = _p.GetTrackingRateHz(); } catch { }
                    double a1 = double.NaN, a2 = double.NaN;
                    try { a1 = _p.GetAxis1Degrees(); } catch { }
                    try { a2 = _p.GetAxis2Degrees(); } catch { }

                    RightAscension = CoordFormat.ParseHours(raS);
                    if (CoordFormat.TryParseDegrees(decS, out var dVal)) Declination = dVal;
                    if (CoordFormat.TryParseDegrees(altS, out var aVal)) Altitude    = aVal;
                    if (CoordFormat.TryParseDegrees(azS,  out var zVal)) Azimuth     = zVal;
                    SiderealTime   = CoordFormat.ParseHours(stS);
                    string priorPier = SideOfPier;
                    SideOfPier     = ExtractPierSide(psS);
                    if (!string.Equals(priorPier ?? "", SideOfPier ?? "", StringComparison.Ordinal))
                    {
                        DebugLogger.Log("PIER",
                            (string.IsNullOrEmpty(priorPier) ? "?" : priorPier) + " -> " +
                            (string.IsNullOrEmpty(SideOfPier) ? "?" : SideOfPier) +
                            " ra=" + RightAscension.ToString("F4", CultureInfo.InvariantCulture) +
                            "h dec=" + Declination.ToString("F3", CultureInfo.InvariantCulture) +
                            "° lst=" + SiderealTime.ToString("F4", CultureInfo.InvariantCulture) +
                            "h gu='" + (gus ?? "").TrimEnd('#') + "'");
                    }
                    LastStatusString = gus ?? "";
                    Axis1Deg = a1;
                    Axis2Deg = a2;

                    // :GU# bytes: 'n'=not tracking, 'N'=not slewing, 'P'=parked,
                    // 'p'=not parked, 'I'=park in progress, 'F'=park failed,
                    // 'H'=at home, 'a'=auto meridian flip enabled.
                    var raw = LastStatusString.TrimEnd('#');
                    Tracking = raw.IndexOf('n') < 0;
                    Slewing  = raw.IndexOf('N') < 0 || raw.IndexOf('I') >= 0;
                    AtPark   = raw.IndexOf('P') >= 0;
                    AtHome   = raw.IndexOf('H') >= 0;
                    AutoMeridianFlip = raw.IndexOf('a') >= 0;
                    TrackingMode = ClassifyTrackingRate(rateHz);

                    // Lazy re-probe for cold-boot mounts where the focuser
                    // axis comes online after the initial :Fa# query. Runs
                    // each cycle until we either find a focuser or burn the
                    // attempt budget.
                    if (!FocuserAvailable && _focuserLateProbeAttempts > 0)
                    {
                        _focuserLateProbeAttempts--;
                        if (TryProbeFocuser())
                        {
                            DebugLogger.Log("FOCUSER",
                                "late probe succeeded with " + _focuserLateProbeAttempts + " cycles remaining");
                            _focuserLateProbeAttempts = 0;
                        }
                    }

                    // Focuser ride-along — every 4th cycle (~3 s at 750 ms) when
                    // a focuser is present. Each value is independently guarded
                    // so a single firmware hiccup doesn't take all three down.
                    if (FocuserAvailable)
                    {
                        _focuserPollTick = (_focuserPollTick + 1) & 0x03;
                        if (_focuserPollTick == 0)
                        {
                            try { FocuserPosition = _p.GetFocuserPositionSteps(); } catch { }
                            try
                            {
                                var ft = _p.GetFocuserStatus();
                                ft = string.IsNullOrEmpty(ft) ? "" : ft.TrimEnd('#');
                                FocuserMoving = ft.Length > 0 && (ft[0] == 'M' || ft[0] == 'm');
                            }
                            catch { }
                            try { FocuserTempC = _p.GetFocuserTemperatureC(); } catch { }
                        }
                    }

                    // Rotator lazy re-probe (cold-boot AXIS3 init).
                    if (!RotatorAvailable && _rotatorLateProbeAttempts > 0)
                    {
                        _rotatorLateProbeAttempts--;
                        if (TryProbeRotator())
                        {
                            DebugLogger.Log("ROTATOR",
                                "late probe succeeded with " + _rotatorLateProbeAttempts + " cycles remaining");
                            _rotatorLateProbeAttempts = 0;
                        }
                    }

                    // Rotator ride-along — same 4th-cycle cadence. Angle + status
                    // are the only frequently-changing fields; capability,
                    // limits, step size, backlash were captured at probe time.
                    if (RotatorAvailable)
                    {
                        _rotatorPollTick = (_rotatorPollTick + 1) & 0x03;
                        if (_rotatorPollTick == 0)
                        {
                            try { RotatorAngleDeg = _p.GetRotatorAngleDeg(); } catch { }
                            try
                            {
                                var rs = RotatorStatus.Parse(_p.GetRotatorStatusRaw());
                                RotatorMoving        = rs.Moving;
                                RotatorDerotating    = rs.Derotating;
                                RotatorDerotReversed = rs.DerotReversed;
                                RotatorRatePreset    = rs.RatePreset;
                            }
                            catch { }
                        }
                    }

                    // Polar Alignment ride-along — same 4th-cycle cadence. Reads
                    // axis 4 + axis 5 positions and moving flags by switching
                    // :FA4#/:FA5# and querying :Fg#/:FT# on each. Restores the
                    // active focuser to axis 4 at the end (arbitrary; nothing
                    // else consumes it in PA mode). Each per-axis read is
                    // independently guarded so a wire blip on one doesn't
                    // blank the other.
                    // PA mode position tracking runs in PaFastPollLoop at
                    // 200ms — separate from this heavy 750ms RA/Dec poll so
                    // NINA TPPA's `?` reads always hit fresh cache.

                    LastUpdateUtc = DateTime.UtcNow;
                    Updated?.Invoke(this, EventArgs.Empty);
                }
                catch
                {
                    // transport error; pause briefly and continue
                }

                try { Task.Delay(_pollMs, ct).Wait(ct); } catch { }
            }
        }

        // Dedicated PA fast poll. Refreshes axis 1 + 2 position cache every
        // 200ms (vs. 750ms main poll). NINA TPPA polls `?` every 300ms; with
        // this cadence the cache is always ≤ 200ms stale → motion is visible
        // every NINA poll, stuck-detector never trips. Each cycle is 4 wire
        // commands (~100ms) under PaAxisLock — held briefly so user jog clicks
        // and the TPPA bridge can interleave on the 100ms idle gap.
        private void PaFastPollLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (!PolarAlignmentMode)
                {
                    // PA mode toggled off live — exit so RefreshPolarAlignmentMode
                    // can spin a fresh task on the next toggle-on without two
                    // concurrent loops fighting for the wire.
                    DebugLogger.Log("PA", "fast poll exit (PolarAlignmentMode=false)");
                    return;
                }
                try
                {
                    lock (PaAxisLock)
                    {
                        ReadPolarAlignmentAxis(1, ref Axis4PositionSteps, ref Axis4Moving);
                        ReadPolarAlignmentAxis(2, ref Axis5PositionSteps, ref Axis5Moving);
                    }
                }
                catch (Exception ex) { DebugLogger.LogException("PA", ex); }
                try { Task.Delay(PaFastPollMs, ct).Wait(ct); } catch { }
            }
        }

        // Read one polar-alignment focuser by INDEX (1..2 in PA mode):
        // select via :FA[n]#, query :Fg# and :FT#. Used by both the panel
        // poll and the TPPA bridge's GRBL `?` status reply. Caller's
        // responsibility to guard the call site — each individual command is
        // wrapped so a single failure is logged and the cached values are
        // left at their prior value.
        private void ReadPolarAlignmentAxis(int focuserIdx, ref int positionField, ref bool movingField)
        {
            // SetActiveFocuser returns false when firmware rejects the index.
            // Bail out and leave the cached value untouched — without this
            // check, both Alt and Az would read whatever focuser happened to
            // already be active.
            bool ok = false;
            try { ok = _p.SetActiveFocuser(focuserIdx); } catch { return; }
            if (!ok) return;
            try { positionField = _p.GetFocuserPositionSteps(); } catch { }
            try
            {
                var ft = _p.GetFocuserStatus();
                ft = string.IsNullOrEmpty(ft) ? "" : ft.TrimEnd('#');
                movingField = ft.Length > 0 && (ft[0] == 'M' || ft[0] == 'm');
            }
            catch { }
        }

        // OnStepX rates: Lunar 57.902 Hz, Solar 60.000, King 60.136, Sidereal 60.164.
        private static string ClassifyTrackingRate(double hz)
        {
            if (hz <= 0.0) return "";
            if (hz < 58.95) return "Lunar";
            if (hz < 60.07) return "Solar";
            if (hz < 60.15) return "King";
            return "Sidereal";
        }

        private static string ExtractPierSide(string reply)
        {
            if (string.IsNullOrEmpty(reply)) return "";
            reply = reply.TrimEnd('#');
            foreach (var c in reply)
            {
                if (c == 'E' || c == 'e') return "E";
                if (c == 'W' || c == 'w') return "W";
            }
            return "";
        }
    }
}
