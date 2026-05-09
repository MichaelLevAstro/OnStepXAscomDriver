using System;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Hardware.State;

namespace ASCOM.OnStepX.ViewModels
{
    // Drives the 3D mount visualizer. Reads from the existing 250 ms poll
    // snapshot — never issues serial commands of its own. Maps HA, Dec, and
    // pier-side to the visualizer's geometric rotation angles; see
    // OnPollSnapshot for the geometry rationale.
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
            // Feed raw mechanical axis angles (:GX42#/:GX43#) straight to the
            // 3D model. OnStepX reports these continuously through park and
            // pier-flip, so the visualizer rotates smoothly with the physical
            // axes — no branching on pier-side, no snap on unpark.
            //
            // Constant 180° offset: OnStepX's instrument convention puts
            // axis1=axis2=0 at the pier-E meridian-equator pose (OTA east of
            // pier, tube south-up). The visualizer geometry's natural
            // raAngle=decAngle=0 pose has the saddle on polar +X (world-west)
            // with the tube along polar -Z. The two conventions differ by a
            // 180° rotation on each axis; the subtraction absorbs that.
            //
            // Older firmware (no :GX42#/:GX43#) leaves Axis1Deg/Axis2Deg as
            // NaN — fall back to mount→instrument transform from HA/Dec/pier.
            if (!double.IsNaN(st.Axis1Deg) && !double.IsNaN(st.Axis2Deg))
            {
                RaAxisAngleDeg  = 180.0 - st.Axis1Deg;
                DecAxisAngleDeg = 180.0 - st.Axis2Deg;
            }
            else
            {
                double ha = st.SiderealTime - st.RightAscension;
                while (ha >  12.0) ha -= 24.0;
                while (ha < -12.0) ha += 24.0;
                bool pierW = string.Equals(st.SideOfPier, "W", StringComparison.OrdinalIgnoreCase);
                double a1 = ha * 15.0 + (pierW ? 180.0 : 0.0);
                double a2 = pierW ? (180.0 - st.Declination) : st.Declination;
                RaAxisAngleDeg  = 180.0 - a1;
                DecAxisAngleDeg = 180.0 - a2;
            }

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
