using System;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Hardware.State;

namespace ASCOM.OnStepX.ViewModels
{
    // Drives the 3D mount visualizer. Reads from the existing 250 ms poll
    // snapshot — never issues serial commands of its own. Prefers the raw
    // mechanical axis angles from MountStateCache.Axis1Deg/Axis2Deg (from
    // :GX42#/:GX43#) when available; falls back to LST−RA and Dec when the
    // firmware doesn't expose those readouts.
    public sealed class VisualizerViewModel : ViewModelBase
    {
        private double _raAxisAngleDeg;
        private double _decAxisAngleDeg;
        private double _siteLatitudeDeg;
        private bool   _isConnected;
        private string _pierSide = "";

        public double RaAxisAngleDeg  { get => _raAxisAngleDeg;  private set => Set(ref _raAxisAngleDeg, value); }
        public double DecAxisAngleDeg { get => _decAxisAngleDeg; private set => Set(ref _decAxisAngleDeg, value); }
        public double SiteLatitudeDeg { get => _siteLatitudeDeg; private set => Set(ref _siteLatitudeDeg, value); }
        public bool   IsConnected     { get => _isConnected;     private set => Set(ref _isConnected, value); }
        public string PierSide        { get => _pierSide;        private set => Set(ref _pierSide, value ?? ""); }

        public VisualizerViewModel()
        {
            SiteLatitudeDeg = DriverSettings.SiteLatitude;
        }

        internal void OnPollSnapshot(MountStateCache st)
        {
            // Prefer mechanical axis angles when the firmware provides them —
            // they already encode pier-side and need no LST math.
            if (!double.IsNaN(st.Axis1Deg))
            {
                RaAxisAngleDeg = st.Axis1Deg;
            }
            else
            {
                double ha = st.SiderealTime - st.RightAscension;
                while (ha >  12.0) ha -= 24.0;
                while (ha < -12.0) ha += 24.0;
                RaAxisAngleDeg = ha * 15.0;
            }

            DecAxisAngleDeg = !double.IsNaN(st.Axis2Deg) ? st.Axis2Deg : st.Declination;

            PierSide = st.SideOfPier;
            SiteLatitudeDeg = DriverSettings.SiteLatitude;
            IsConnected = true;
        }

        public void OnDisconnected()
        {
            IsConnected = false;
            // Keep last pose visible — feels nicer than snapping to zero.
        }
    }
}
