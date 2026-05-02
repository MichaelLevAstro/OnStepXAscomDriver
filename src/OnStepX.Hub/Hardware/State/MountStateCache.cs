using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
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

        public event EventHandler Updated;

        private readonly LX200Protocol _p;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task _pollTask;
        private int _pollMs;

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
            _pollTask = Task.Run(() => PollLoop(_cts.Token));
        }

        public void Stop()
        {
            _cts.Cancel();
            try { _pollTask?.Wait(1000); } catch { }
            _pollTask = null;
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
