using System;
using System.Globalization;

namespace ASCOM.OnStepX.Hardware
{
    internal static class CoordFormat
    {
        // RA hours "HH:MM:SS.SSSS#" or "HH:MM:SS#"
        public static double ParseHours(string s)
        {
            s = Strip(s);
            var parts = s.Split(':');
            double h = double.Parse(parts[0], CultureInfo.InvariantCulture);
            double m = parts.Length > 1 ? double.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
            double sec = parts.Length > 2 ? double.Parse(parts[2], CultureInfo.InvariantCulture) : 0;
            double sign = h < 0 ? -1 : 1;
            return sign * (Math.Abs(h) + m / 60.0 + sec / 3600.0);
        }

        // Degrees "sDD*MM:SS.SSS#" or "sDD*MM#"
        public static double ParseDegrees(string s)
        {
            if (!TryParseDegrees(s, out var v))
                throw new FormatException("Could not parse degree value: '" + (s ?? "") + "'");
            return v;
        }

        public static bool TryParseDegrees(string s, out double degrees)
        {
            degrees = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = Strip(s)
                .Replace('*', ':').Replace((char)223, ':').Replace('\u00B0', ':')
                .Replace('\'', ':').Replace('\u2032', ':')
                .Replace("\"", "").Replace("\u2033", "")
                .Replace(" ", "");
            if (s.Length == 0) return false;
            double sign = 1;
            if (s.StartsWith("-")) { sign = -1; s = s.Substring(1); }
            else if (s.StartsWith("+")) s = s.Substring(1);
            s = s.TrimEnd(':');
            var parts = s.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return false;
            double m = 0, sec = 0;
            if (parts.Length > 1 && !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out m)) return false;
            if (parts.Length > 2 && !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out sec)) return false;
            degrees = sign * (d + m / 60.0 + sec / 3600.0);
            return true;
        }

        public static string FormatHoursHighPrec(double hours)
        {
            hours = ((hours % 24) + 24) % 24;
            int h = (int)hours;
            double rem = (hours - h) * 60.0;
            int m = (int)rem;
            double s = (rem - m) * 60.0;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00.0000}", h, m, s);
        }

        public static string FormatDegreesHighPrec(double deg)
        {
            char sign = deg < 0 ? '-' : '+';
            deg = Math.Abs(deg);
            int d = (int)deg;
            double rem = (deg - d) * 60.0;
            int m = (int)rem;
            double s = (rem - m) * 60.0;
            return string.Format(CultureInfo.InvariantCulture, "{0}{1:00}*{2:00}:{3:00.000}", sign, d, m, s);
        }

        // LX200 high-precision format for mount commands (:Sd/:St/:Sa).
        // OnStepX parses integer seconds (%d:%d:%d); decimal seconds cause the mount
        // to reply with '0' (rejected), so always round to whole seconds here.
        public static string FormatDegreesMount(double deg)
        {
            char sign = deg < 0 ? '-' : '+';
            deg = Math.Abs(deg);
            int d = (int)deg;
            double rem = (deg - d) * 60.0;
            int m = (int)rem;
            int s = (int)Math.Round((rem - m) * 60.0);
            if (s >= 60) { s = 0; m++; }
            if (m >= 60) { m = 0; d++; }
            return string.Format(CultureInfo.InvariantCulture, "{0}{1:00}*{2:00}:{3:00}", sign, d, m, s);
        }

        // OnStepX-Extended also accepts signed decimal degrees for :St/:Sg/:Sd/:Sa,
        // which preserves sub-arcsec precision that DMS integer-seconds throws away.
        // 6 fractional digits ≈ 0.004″. Use this as the preferred write format; fall
        // back to FormatDegreesMount (DMS) on older firmware that rejects decimals.
        public static string FormatDegreesDecimal(double deg) =>
            string.Format(CultureInfo.InvariantCulture, "{0}{1:00.000000}",
                deg < 0 ? "-" : "+", Math.Abs(deg));

        public static string FormatLongitudeDecimal(double deg) =>
            string.Format(CultureInfo.InvariantCulture, "{0}{1:000.000000}",
                deg < 0 ? "-" : "+", Math.Abs(deg));

        // User-facing DMS "+DD°MM'SS"" / "-DD°MM'SS"" for latitude, "+DDD°..." for longitude.
        public static string FormatLatitudeDms(double deg)  => FormatDms(deg, 2);
        public static string FormatLongitudeDms(double deg) => FormatDms(deg, 3);

        private static string FormatDms(double deg, int degWidth)
        {
            char sign = deg < 0 ? '-' : '+';
            deg = Math.Abs(deg);
            int d = (int)deg;
            double rem = (deg - d) * 60.0;
            int m = (int)rem;
            int s = (int)Math.Round((rem - m) * 60.0);
            if (s == 60) { s = 0; m++; }
            if (m == 60) { m = 0; d++; }
            string fmt = "{0}{1:" + new string('0', degWidth) + "}\u00B0{2:00}'{3:00}\"";
            return string.Format(CultureInfo.InvariantCulture, fmt, sign, d, m, s);
        }

        public static string FormatLongitudeHighPrec(double deg)
        {
            char sign = deg < 0 ? '-' : '+';
            deg = Math.Abs(deg);
            int d = (int)deg;
            double rem = (deg - d) * 60.0;
            int m = (int)rem;
            double s = (rem - m) * 60.0;
            return string.Format(CultureInfo.InvariantCulture, "{0}{1:000}*{2:00}:{3:00}", sign, d, m, (int)s);
        }

        private static string Strip(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim().TrimEnd('#');
            return s;
        }
    }
}
