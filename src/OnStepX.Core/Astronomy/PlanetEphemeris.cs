using System;

namespace ASCOM.OnStepX.Astronomy
{
    internal enum Body { Sun, Moon, Mercury, Venus, Mars, Jupiter, Saturn, Uranus, Neptune }

    // Low-precision geocentric ephemeris based on Paul Schlyter's "ppcomp" algorithms.
    // Accuracy: ~1-2 arcmin for Sun/planets, ~5-10 arcmin for Moon with perturbations.
    // Valid roughly 1900-2100. Output is apparent RA/Dec of date.
    internal static class PlanetEphemeris
    {
        private const double D2R = Math.PI / 180.0;
        private const double R2D = 180.0 / Math.PI;

        public static void Compute(Body body, DateTime utc, out double raHours, out double decDeg)
        {
            double d = DayNumber(utc);
            double ecl = 23.4393 - 3.563e-7 * d;

            // Sun / Earth's orbit
            double sunMs = Norm(356.0470 + 0.9856002585 * d);
            double sunWs = Norm(282.9404 + 4.70935e-5 * d);
            double sunE  = EccentricAnom(sunMs, 0.016709);
            double sunXv = Math.Cos(sunE * D2R) - 0.016709;
            double sunYv = Math.Sqrt(1 - 0.016709 * 0.016709) * Math.Sin(sunE * D2R);
            double sunV  = Math.Atan2(sunYv, sunXv) * R2D;
            double sunR  = Math.Sqrt(sunXv * sunXv + sunYv * sunYv);
            double sunLon = Norm(sunV + sunWs);
            double sunX = sunR * Math.Cos(sunLon * D2R);
            double sunY = sunR * Math.Sin(sunLon * D2R);

            double xEcl, yEcl, zEcl;

            if (body == Body.Sun)
            {
                xEcl = sunX; yEcl = sunY; zEcl = 0;
            }
            else if (body == Body.Moon)
            {
                double N = Norm(125.1228 - 0.0529538083 * d);
                double i = 5.1454;
                double w = Norm(318.0634 + 0.1643573223 * d);
                double a = 60.2666;
                double e = 0.054900;
                double M = Norm(115.3654 + 13.0649929509 * d);

                double lon, lat, r;
                HelioEcliptic(N, i, w, a, e, M, out lon, out lat, out r);

                double Lm = Norm(N + w + M);
                double Ls = Norm(sunWs + sunMs);
                double Dm = Lm - Ls;
                double F  = Lm - N;
                double Mm = M;
                double Ms = sunMs;

                lon += -1.274 * Math.Sin((Mm - 2 * Dm) * D2R)
                       + 0.658 * Math.Sin(2 * Dm * D2R)
                       - 0.186 * Math.Sin(Ms * D2R)
                       - 0.059 * Math.Sin((2 * Mm - 2 * Dm) * D2R)
                       - 0.057 * Math.Sin((Mm - 2 * Dm + Ms) * D2R)
                       + 0.053 * Math.Sin((Mm + 2 * Dm) * D2R)
                       + 0.046 * Math.Sin((2 * Dm - Ms) * D2R)
                       + 0.041 * Math.Sin((Mm - Ms) * D2R)
                       - 0.035 * Math.Sin(Dm * D2R)
                       - 0.031 * Math.Sin((Mm + Ms) * D2R)
                       - 0.015 * Math.Sin((2 * F - 2 * Dm) * D2R)
                       + 0.011 * Math.Sin((Mm - 4 * Dm) * D2R);
                lat += -0.173 * Math.Sin((F - 2 * Dm) * D2R)
                       - 0.055 * Math.Sin((Mm - F - 2 * Dm) * D2R)
                       - 0.046 * Math.Sin((Mm + F - 2 * Dm) * D2R)
                       + 0.033 * Math.Sin((F + 2 * Dm) * D2R)
                       + 0.017 * Math.Sin((2 * Mm + F) * D2R);
                r   += -0.58 * Math.Cos((Mm - 2 * Dm) * D2R)
                       - 0.46 * Math.Cos(2 * Dm * D2R);

                xEcl = r * Math.Cos(lon * D2R) * Math.Cos(lat * D2R);
                yEcl = r * Math.Sin(lon * D2R) * Math.Cos(lat * D2R);
                zEcl = r * Math.Sin(lat * D2R);
            }
            else
            {
                double[] el = PlanetElements(body, d);
                double lon, lat, r;
                HelioEcliptic(el[0], el[1], el[2], el[3], el[4], el[5], out lon, out lat, out r);
                double xh = r * Math.Cos(lon * D2R) * Math.Cos(lat * D2R);
                double yh = r * Math.Sin(lon * D2R) * Math.Cos(lat * D2R);
                double zh = r * Math.Sin(lat * D2R);
                xEcl = xh + sunX;
                yEcl = yh + sunY;
                zEcl = zh;
            }

            double xe = xEcl;
            double ye = yEcl * Math.Cos(ecl * D2R) - zEcl * Math.Sin(ecl * D2R);
            double ze = yEcl * Math.Sin(ecl * D2R) + zEcl * Math.Cos(ecl * D2R);
            double ra = Math.Atan2(ye, xe) * R2D;
            double dec = Math.Atan2(ze, Math.Sqrt(xe * xe + ye * ye)) * R2D;
            ra = ((ra % 360) + 360) % 360;
            raHours = ra / 15.0;
            decDeg = dec;
        }

        private static void HelioEcliptic(double N, double i, double w, double a, double e, double M,
            out double lonDeg, out double latDeg, out double rAU)
        {
            double E = EccentricAnom(M, e);
            double xv = a * (Math.Cos(E * D2R) - e);
            double yv = a * Math.Sqrt(1 - e * e) * Math.Sin(E * D2R);
            double v = Math.Atan2(yv, xv) * R2D;
            double r = Math.Sqrt(xv * xv + yv * yv);
            double lon = Norm(v + w);
            double Nr = N * D2R, lonR = lon * D2R, iR = i * D2R;
            double xh = r * (Math.Cos(Nr) * Math.Cos(lonR) - Math.Sin(Nr) * Math.Sin(lonR) * Math.Cos(iR));
            double yh = r * (Math.Sin(Nr) * Math.Cos(lonR) + Math.Cos(Nr) * Math.Sin(lonR) * Math.Cos(iR));
            double zh = r * Math.Sin(lonR) * Math.Sin(iR);
            lonDeg = Norm(Math.Atan2(yh, xh) * R2D);
            latDeg = Math.Atan2(zh, Math.Sqrt(xh * xh + yh * yh)) * R2D;
            rAU = Math.Sqrt(xh * xh + yh * yh + zh * zh);
        }

        private static double EccentricAnom(double Mdeg, double e)
        {
            double M = Mdeg * D2R;
            double E = M + e * Math.Sin(M) * (1 + e * Math.Cos(M));
            for (int k = 0; k < 30; k++)
            {
                double dE = (E - e * Math.Sin(E) - M) / (1 - e * Math.Cos(E));
                E -= dE;
                if (Math.Abs(dE) < 1e-9) break;
            }
            return E * R2D;
        }

        private static double[] PlanetElements(Body b, double d)
        {
            switch (b)
            {
                case Body.Mercury: return new[] { Norm(48.3313 + 3.24587e-5 * d), 7.0047 + 5.00e-8 * d, Norm(29.1241 + 1.01444e-5 * d), 0.387098, 0.205635 + 5.59e-10 * d, Norm(168.6562 + 4.0923344368 * d) };
                case Body.Venus:   return new[] { Norm(76.6799 + 2.46590e-5 * d), 3.3946 + 2.75e-8 * d, Norm(54.8910 + 1.38374e-5 * d), 0.723330, 0.006773 - 1.302e-9 * d, Norm(48.0052 + 1.6021302244 * d) };
                case Body.Mars:    return new[] { Norm(49.5574 + 2.11081e-5 * d), 1.8497 - 1.78e-8 * d, Norm(286.5016 + 2.92961e-5 * d), 1.523688, 0.093405 + 2.516e-9 * d, Norm(18.6021 + 0.5240207766 * d) };
                case Body.Jupiter: return new[] { Norm(100.4542 + 2.76854e-5 * d), 1.3030 - 1.557e-7 * d, Norm(273.8777 + 1.64505e-5 * d), 5.20256, 0.048498 + 4.469e-9 * d, Norm(19.8950 + 0.0830853001 * d) };
                case Body.Saturn:  return new[] { Norm(113.6634 + 2.38980e-5 * d), 2.4886 - 1.081e-7 * d, Norm(339.3939 + 2.97661e-5 * d), 9.55475, 0.055546 - 9.499e-9 * d, Norm(316.9670 + 0.0334442282 * d) };
                case Body.Uranus:  return new[] { Norm(74.0005 + 1.3978e-5 * d), 0.7733 + 1.9e-8 * d, Norm(96.6612 + 3.0565e-5 * d), 19.18171 - 1.55e-8 * d, 0.047318 + 7.45e-9 * d, Norm(142.5905 + 0.011725806 * d) };
                case Body.Neptune: return new[] { Norm(131.7806 + 3.0173e-5 * d), 1.7700 - 2.55e-7 * d, Norm(272.8461 - 6.027e-6 * d), 30.05826 + 3.313e-8 * d, 0.008606 + 2.15e-9 * d, Norm(260.2471 + 0.005995147 * d) };
            }
            return new double[6];
        }

        private static double Norm(double deg) => ((deg % 360) + 360) % 360;

        private static double DayNumber(DateTime utc)
        {
            var u = utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime();
            int Y = u.Year, M = u.Month, D = u.Day;
            double UT = u.Hour + u.Minute / 60.0 + u.Second / 3600.0;
            int dInt = 367 * Y - 7 * (Y + (M + 9) / 12) / 4 + 275 * M / 9 + D - 730530;
            return dInt + UT / 24.0;
        }
    }
}
