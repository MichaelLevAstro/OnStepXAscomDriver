using System;
using System.Globalization;
using ASCOM.OnStepX.Hardware;

namespace ASCOM.OnStepX.Astronomy
{
    // Canonical UTC clock for the mount. Solar-system ephemeris uses this
    // instead of DateTime.UtcNow so a skewed PC clock does not shift planet
    // positions (Moon ≈ 0.5″/sec → 30s skew = 15″ pointing error).
    //
    // Strategy: prime a (mountUtc − pcUtc) delta from :GC# / :GL# / :GG#
    // once after connect, refresh every 60s. Inter-query calls are cheap —
    // just DateTime.UtcNow + cachedDelta. On any failure we keep the last
    // good delta; if never primed, fall through to DateTime.UtcNow.
    internal static class MountTime
    {
        private static readonly object _gate = new object();
        private static TimeSpan _cachedDelta = TimeSpan.Zero;
        private static bool _primed;
        private static DateTime _lastRefreshUtc = DateTime.MinValue;

        public static DateTime NowUtc(MountSession mount)
        {
            var pcUtc = DateTime.UtcNow;
            if (mount == null || !mount.IsOpen)
            {
                // Drop primed state on disconnect so a reconnect re-primes
                // from the new mount instead of trusting a stale delta.
                lock (_gate) { _primed = false; }
                return pcUtc;
            }

            bool needRefresh;
            lock (_gate)
            {
                needRefresh = !_primed || (pcUtc - _lastRefreshUtc).TotalSeconds >= 60.0;
            }

            if (needRefresh)
                TryRefresh(mount, pcUtc);

            lock (_gate)
            {
                return _primed ? pcUtc + _cachedDelta : pcUtc;
            }
        }

        private static void TryRefresh(MountSession mount, DateTime pcUtc)
        {
            try
            {
                var p = mount.Protocol;
                if (p == null) return;

                string dateRaw = p.GetDate();          // "MM/DD/YY#"
                string timeRaw = p.GetLocalTime();     // "HH:MM:SS#"
                string offsetRaw = p.GetUtcOffset();   // "+HH:MM#" west-positive

                if (!TryParseLocal(dateRaw, timeRaw, out var local)) return;
                if (!TryParseOffsetEastHours(offsetRaw, out var eastHours)) return;

                // Mount stores civil offset west-positive (Meade :SG/:GG). East-positive
                // is what DateTime arithmetic expects.
                var mountUtc = new DateTime(
                    local.Year, local.Month, local.Day,
                    local.Hour, local.Minute, local.Second,
                    DateTimeKind.Unspecified) - TimeSpan.FromHours(eastHours);

                lock (_gate)
                {
                    _cachedDelta = mountUtc - pcUtc;
                    _primed = true;
                    _lastRefreshUtc = pcUtc;
                }
            }
            catch
            {
                // Keep last-good delta. Don't poison the cache on a transient read.
            }
        }

        private static bool TryParseLocal(string dateRaw, string timeRaw, out DateTime local)
        {
            local = default;
            string d = Strip(dateRaw);
            string t = Strip(timeRaw);
            if (d.Length == 0 || t.Length == 0) return false;

            // OnStepX :GC# is MM/dd/yy. Two-digit year — assume 2000s (mount epoch).
            if (!DateTime.TryParseExact(d, "MM/dd/yy",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return false;
            if (!TimeSpan.TryParseExact(t, @"hh\:mm\:ss",
                    CultureInfo.InvariantCulture, out var time))
                return false;
            local = date.Date + time;
            return true;
        }

        private static bool TryParseOffsetEastHours(string raw, out double eastHours)
        {
            eastHours = 0;
            // CoordFormat handles HH:MM or decimal hours, with sign. Mount value is
            // west-positive (Meade convention) — flip sign to get east-positive.
            if (!CoordFormat.TryParseDegrees(raw, out var westPos)) return false;
            eastHours = -westPos;
            return true;
        }

        private static string Strip(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Trim().TrimEnd('#').Trim();
        }
    }
}
