using System.Globalization;

namespace ASCOM.OnStepX.ViewModels
{
    // One alignment-model star: the true sky coordinate, where the mount actually
    // pointed, the pier side, and the derived horizontal position used to plot it
    // on the dome. Coordinates are fixed at read time; only IsSelected changes.
    public sealed class SkyModelPoint : ViewModelBase
    {
        public int Number { get; }
        public double ActualHaHours { get; }
        public double ActualDecDeg { get; }
        public double MountHaHours { get; }
        public double MountDecDeg { get; }
        public int PierSide { get; }          // +1 = East, -1 = West
        public double AltDeg { get; }
        public double AzDeg { get; }
        public double ErrorArcsec { get; }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

        public SkyModelPoint(int number,
                             double actualHaHours, double actualDecDeg,
                             double mountHaHours, double mountDecDeg,
                             int pierSide, double altDeg, double azDeg, double errorArcsec)
        {
            Number = number;
            ActualHaHours = actualHaHours;
            ActualDecDeg = actualDecDeg;
            MountHaHours = mountHaHours;
            MountDecDeg = mountDecDeg;
            PierSide = pierSide;
            AltDeg = altDeg;
            AzDeg = azDeg;
            ErrorArcsec = errorArcsec;
        }

        public string PierText => PierSide > 0 ? "E" : (PierSide < 0 ? "W" : "?");

        public string AltAzText =>
            string.Format(CultureInfo.InvariantCulture, "Alt {0:F1}°  Az {1:F1}°", AltDeg, AzDeg);

        public string ErrorText =>
            ErrorArcsec >= 600.0
                ? string.Format(CultureInfo.InvariantCulture, "{0:F1}'", ErrorArcsec / 60.0)
                : string.Format(CultureInfo.InvariantCulture, "{0:F0}\"", ErrorArcsec);
    }
}
