using System;
using System.Globalization;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Hardware;
using ASCOM.OnStepX.Hardware.State;

namespace ASCOM.OnStepX.ViewModels
{
    // Read-only Current Position card. Mirrors HubForm.BuildPositionGroup +
    // RefreshStatus formatting. Driven entirely by 250 ms poll snapshot.
    public sealed class PositionViewModel : ViewModelBase
    {
        private string _ra = "—";
        private string _dec = "—";
        private string _alt = "—";
        private string _az = "—";
        private string _pierSide = "—";
        private string _lst = "—";

        public string RightAscension { get => _ra; private set => Set(ref _ra, value); }
        public string Declination    { get => _dec; private set => Set(ref _dec, value); }
        public string Altitude       { get => _alt; private set => Set(ref _alt, value); }
        public string Azimuth        { get => _az; private set => Set(ref _az, value); }
        public string PierSide       { get => _pierSide; private set => Set(ref _pierSide, value); }
        public string Lst            { get => _lst; private set => Set(ref _lst, value); }

        internal void OnPollSnapshot(MountStateCache st)
        {
            RightAscension = CoordFormat.FormatHoursHighPrec(st.RightAscension);
            Declination = CoordFormat.FormatDegreesHighPrec(st.Declination);
            Altitude = st.Altitude.ToString("F2", CultureInfo.InvariantCulture) + "°";
            Azimuth  = st.Azimuth.ToString("F2", CultureInfo.InvariantCulture) + "°";
            PierSide = string.IsNullOrEmpty(st.SideOfPier) ? "—" : st.SideOfPier;

            double skyLst = ComputeSkyLstHours(DriverSettings.SiteLongitude);
            double mountLst = st.SiderealTime;
            double dhHours = mountLst - skyLst;
            while (dhHours > 12) dhHours -= 24;
            while (dhHours < -12) dhHours += 24;
            double deltaMin = dhHours * 60.0;
            Lst = FormatHours(mountLst) + "  /  " + FormatHours(skyLst)
                  + "   Δ " + deltaMin.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " min";
        }

        public void OnDisconnected()
        {
            RightAscension = Declination = Altitude = Azimuth = PierSide = Lst = "—";
        }

        // Mean GMST formula (Meeus, ch. 12). Identical to HubForm.ComputeSkyLstHours.
        private static double ComputeSkyLstHours(double eastLonDeg)
        {
            var utc = DateTime.UtcNow;
            double jd = utc.ToOADate() + 2415018.5;
            double d = jd - 2451545.0;
            double t = d / 36525.0;
            double gmstDeg = 280.46061837 + 360.98564736629 * d + 0.000387933 * t * t - (t * t * t) / 38710000.0;
            double lstDeg = gmstDeg + eastLonDeg;
            lstDeg = ((lstDeg % 360.0) + 360.0) % 360.0;
            return lstDeg / 15.0;
        }

        private static string FormatHours(double h)
        {
            h = ((h % 24.0) + 24.0) % 24.0;
            int hh = (int)h;
            double remMin = (h - hh) * 60.0;
            int mm = (int)remMin;
            double ss = (remMin - mm) * 60.0;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00.0}", hh, mm, ss);
        }
    }
}
