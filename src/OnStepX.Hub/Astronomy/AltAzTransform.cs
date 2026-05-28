using System;

namespace ASCOM.OnStepX.Astronomy
{
    // Spherical transforms for plotting alignment-model points on the sky dome.
    // Hour angle is already meridian-relative, so no sidereal time is needed —
    // only the observer's latitude.
    internal static class AltAzTransform
    {
        private const double Deg2Rad = Math.PI / 180.0;
        private const double Rad2Deg = 180.0 / Math.PI;
        private const double HoursToDeg = 15.0;

        // (HA, Dec) + latitude -> (altitude, azimuth). Azimuth is measured from
        // North increasing toward East (N = 0, E = 90, S = 180, W = 270).
        public static void ToAltAz(double haHours, double decDeg, double latDeg,
                                   out double altDeg, out double azDeg)
        {
            double h = haHours * HoursToDeg * Deg2Rad;
            double dec = decDeg * Deg2Rad;
            double lat = latDeg * Deg2Rad;

            double sinDec = Math.Sin(dec), cosDec = Math.Cos(dec);
            double sinLat = Math.Sin(lat), cosLat = Math.Cos(lat);
            double cosH = Math.Cos(h), sinH = Math.Sin(h);

            double sinAlt = sinLat * sinDec + cosLat * cosDec * cosH;
            if (sinAlt > 1.0) sinAlt = 1.0; else if (sinAlt < -1.0) sinAlt = -1.0;
            altDeg = Math.Asin(sinAlt) * Rad2Deg;

            double north = cosLat * sinDec - sinLat * cosDec * cosH;
            double east = -cosDec * sinH;
            double az = Math.Atan2(east, north) * Rad2Deg;
            if (az < 0) az += 360.0;
            azDeg = az;
        }

        // Great-circle separation between two (HA, Dec) directions, in arcseconds.
        // Haversine form keeps precision for the small separations (arcsec..arcmin)
        // that a pointing-model residual produces.
        public static double AngularSeparationArcsec(double haHoursA, double decDegA,
                                                      double haHoursB, double decDegB)
        {
            double decA = decDegA * Deg2Rad, decB = decDegB * Deg2Rad;
            double dH = (haHoursA - haHoursB) * HoursToDeg * Deg2Rad;
            double dDec = decA - decB;

            double sinHalfDec = Math.Sin(dDec / 2.0);
            double sinHalfH = Math.Sin(dH / 2.0);
            double a = sinHalfDec * sinHalfDec +
                       Math.Cos(decA) * Math.Cos(decB) * sinHalfH * sinHalfH;
            if (a < 0) a = 0; else if (a > 1) a = 1;
            double sepRad = 2.0 * Math.Asin(Math.Sqrt(a));
            return sepRad * Rad2Deg * 3600.0;
        }
    }
}
