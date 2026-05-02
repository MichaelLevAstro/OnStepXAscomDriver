using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using ASCOM.OnStepX.ViewModels;
using HelixToolkit.Wpf;

namespace ASCOM.OnStepX.Controls
{
    // Live 3D visualization of a German Equatorial Mount. Geometry is built
    // from primitives (cylinders + boxes); two RotateTransform3D instances
    // — one per axis — are mutated by animations driven from the bound
    // VisualizerViewModel. No serial commands; the VM is fed by the
    // existing 250 ms poll snapshot in MainViewModel.
    public partial class MountVisualizer : UserControl
    {
        // --- Tunable scene dimensions (arbitrary units; HelixViewport ZoomExtents fits).
        private const double PierHeight       = 1.6;
        private const double PierWidth        = 0.30;
        private const double PierDepth        = 0.30;
        private const double RaShaftLength    = 1.10;
        private const double RaShaftRadius    = 0.085;
        private const double SaddleLength     = 0.45;
        private const double SaddleRadius     = 0.075;
        private const double CtwShaftLength   = 0.95;
        private const double CtwShaftRadius   = 0.045;
        private const double CtwBobRadius     = 0.16;
        private const double OtaLength        = 1.05;
        private const double OtaRadius        = 0.135;
        private const double GroundRadius     = 1.6;

        // --- Animation
        private static readonly Duration EaseDuration = new Duration(TimeSpan.FromMilliseconds(750));
        private static readonly IEasingFunction Easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        // --- Mutable scene transforms. These get their AxisAngleRotation3D
        // rebuilt from the proxy DP callbacks; the Helix viewport sees them
        // change and re-renders.
        private RotateTransform3D _latitudeRotation;
        private RotateTransform3D _raAxisRotation;
        private RotateTransform3D _decAxisRotation;

        // --- VM subscription
        private VisualizerViewModel _vm;

        public MountVisualizer()
        {
            InitializeComponent();
            BuildScene();
            DataContextChanged += OnDataContextChanged;
            Loaded += (_, __) => ApplyVmSnapshot();
            Unloaded += (_, __) => DetachVm();
        }

        // ── Proxy DPs animated by DoubleAnimation; their callbacks
        //    rebuild the AxisAngleRotation3D since Rotation3D.Angle isn't
        //    directly animatable through Freezable rules.
        public static readonly DependencyProperty RaAngleProperty = DependencyProperty.Register(
            nameof(RaAngle), typeof(double), typeof(MountVisualizer),
            new PropertyMetadata(0.0, (d, e) => ((MountVisualizer)d).UpdateRaRotation()));
        public double RaAngle { get => (double)GetValue(RaAngleProperty); set => SetValue(RaAngleProperty, value); }

        public static readonly DependencyProperty DecAngleProperty = DependencyProperty.Register(
            nameof(DecAngle), typeof(double), typeof(MountVisualizer),
            new PropertyMetadata(0.0, (d, e) => ((MountVisualizer)d).UpdateDecRotation()));
        public double DecAngle { get => (double)GetValue(DecAngleProperty); set => SetValue(DecAngleProperty, value); }

        public static readonly DependencyProperty LatitudeAngleProperty = DependencyProperty.Register(
            nameof(LatitudeAngle), typeof(double), typeof(MountVisualizer),
            new PropertyMetadata(45.0, (d, e) => ((MountVisualizer)d).UpdateLatitudeRotation()));
        public double LatitudeAngle { get => (double)GetValue(LatitudeAngleProperty); set => SetValue(LatitudeAngleProperty, value); }

        private void UpdateRaRotation()
        {
            if (_raAxisRotation == null) return;
            _raAxisRotation.Rotation = new AxisAngleRotation3D(new Vector3D(0, 1, 0), RaAngle);
        }

        private void UpdateDecRotation()
        {
            if (_decAxisRotation == null) return;
            _decAxisRotation.Rotation = new AxisAngleRotation3D(new Vector3D(1, 0, 0), DecAngle);
        }

        private void UpdateLatitudeRotation()
        {
            if (_latitudeRotation == null) return;
            // Tilt the polar frame so its +Y axis points to the celestial pole.
            // World: Y = up, Z = north. Rotation about world +X by (90 - lat)
            // takes polar +Y from world (0,1,0) into world (0, sin lat, cos lat),
            // i.e. up-and-north — the NCP for a northern-hemisphere site.
            _latitudeRotation.Rotation = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 90.0 - LatitudeAngle);
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            DetachVm();
            _vm = e.NewValue as VisualizerViewModel;
            if (_vm == null) return;
            _vm.PropertyChanged += OnVmPropertyChanged;
            ApplyVmSnapshot();
        }

        private void DetachVm()
        {
            if (_vm == null) return;
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm = null;
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(VisualizerViewModel.RaAxisAngleDeg):
                    AnimateToRa(_vm.RaAxisAngleDeg);
                    break;
                case nameof(VisualizerViewModel.DecAxisAngleDeg):
                    AnimateToDec(_vm.DecAxisAngleDeg);
                    break;
                case nameof(VisualizerViewModel.SiteLatitudeDeg):
                    LatitudeAngle = _vm.SiteLatitudeDeg;
                    break;
            }
        }

        private void ApplyVmSnapshot()
        {
            if (_vm == null) return;
            LatitudeAngle = _vm.SiteLatitudeDeg;
            // Snap on first attach — no animation surprise.
            BeginAnimation(RaAngleProperty, null);
            RaAngle = _vm.RaAxisAngleDeg;
            BeginAnimation(DecAngleProperty, null);
            DecAngle = _vm.DecAxisAngleDeg;
        }

        private void AnimateToRa(double newAngleDeg)
        {
            double current = RaAngle;
            double target = newAngleDeg;
            // Pick the short way around — RA wraps every 360°.
            while (target - current >  180.0) target -= 360.0;
            while (target - current < -180.0) target += 360.0;
            var anim = new DoubleAnimation
            {
                To = target,
                Duration = EaseDuration,
                EasingFunction = Easing,
                FillBehavior = FillBehavior.HoldEnd
            };
            BeginAnimation(RaAngleProperty, anim);
        }

        private void AnimateToDec(double newAngleDeg)
        {
            // Dec stays in roughly [-90, +90] but the firmware can report up to
            // ±180° on a GEM that's mid-flip. Take the short way regardless.
            double current = DecAngle;
            double target = newAngleDeg;
            while (target - current >  180.0) target -= 360.0;
            while (target - current < -180.0) target += 360.0;
            var anim = new DoubleAnimation
            {
                To = target,
                Duration = EaseDuration,
                EasingFunction = Easing,
                FillBehavior = FillBehavior.HoldEnd
            };
            BeginAnimation(DecAngleProperty, anim);
        }

        // ─── Scene construction ───────────────────────────────────────────

        private void BuildScene()
        {
            // World convention: Y = up, Z = north (toward celestial pole projection),
            // X = east. Pier sits at world origin on the ground plane.

            var pierColor   = ResolveColor("Brush.BorderStrong", Color.FromRgb(0x3a, 0x42, 0x50));
            var raColor     = ResolveColor("Brush.Border",       Color.FromRgb(0x2a, 0x31, 0x3c));
            var saddleColor = ResolveColor("Brush.TextDim",      Color.FromRgb(0x9a, 0xa4, 0xb2));
            var otaColor    = ResolveColor("Brush.Accent",       Color.FromRgb(0xe5, 0x48, 0x2d));
            var frontColor  = ResolveColor("Brush.Info",         Color.FromRgb(0x5f, 0x9e, 0xd4));
            var lensColor   = ResolveColor("Brush.Ok",           Color.FromRgb(0x4a, 0xc2, 0x7a));
            var ctwColor    = ResolveColor("Brush.BorderStrong", Color.FromRgb(0x3a, 0x42, 0x50));
            var groundColor = ResolveColor("Brush.Panel",        Color.FromRgb(0x1b, 0x1f, 0x26));
            var northColor  = ResolveColor("Brush.Info",         Color.FromRgb(0x5f, 0x9e, 0xd4));

            var pierMat   = MakeMaterial(pierColor);
            var raMat     = MakeMaterial(raColor);
            var saddleMat = MakeMaterial(saddleColor);
            var otaMat    = MakeMaterial(otaColor);
            var frontMat  = MakeMaterial(frontColor);
            var lensMat   = MakeMaterial(lensColor);
            var ctwMat    = MakeMaterial(ctwColor);
            var groundMat = MakeMaterial(groundColor);
            var northMat  = MakeMaterial(northColor);

            // Ground disc — flat circle at y=0
            var groundMb = new MeshBuilder();
            groundMb.AddCylinder(new Point3D(0, -0.005, 0), new Point3D(0, 0.0, 0), GroundRadius, 48);
            Viewport.Children.Add(MakeVisual(groundMb, groundMat));

            // North arrow on the ground (small triangle along +Z)
            var northMb = new MeshBuilder();
            northMb.AddBox(new Point3D(0, 0.005, GroundRadius * 0.55), 0.05, 0.005, 0.55);
            northMb.AddCone(new Point3D(0, 0.005, GroundRadius * 0.85), new Vector3D(0, 0, 1), 0.09, 0.09, 0.18, true, true, 16);
            Viewport.Children.Add(MakeVisual(northMb, northMat));

            // Pier — a vertical box from y=0 up to y=PierHeight
            var pierMb = new MeshBuilder();
            pierMb.AddBox(new Point3D(0, PierHeight / 2.0, 0), PierWidth, PierHeight, PierDepth);
            Viewport.Children.Add(MakeVisual(pierMb, pierMat));

            // ── Polar frame: tilts the RA shaft to point at the celestial pole.
            //    Rotation about world X by (90 - LatitudeAngle).
            _latitudeRotation = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 45.0));
            UpdateLatitudeRotation();

            // The polar frame's origin sits at the top of the pier.
            var polarTransform = new Transform3DGroup();
            polarTransform.Children.Add(_latitudeRotation);
            polarTransform.Children.Add(new TranslateTransform3D(0, PierHeight, 0));

            var polarFrame = new ModelVisual3D { Transform = polarTransform };
            Viewport.Children.Add(polarFrame);

            // ── Static RA shaft (cylinder along polar Y, centered on origin).
            var raShaftMb = new MeshBuilder();
            raShaftMb.AddCylinder(
                new Point3D(0, -RaShaftLength * 0.45, 0),
                new Point3D(0,  RaShaftLength * 0.45, 0),
                RaShaftRadius, 24);
            polarFrame.Children.Add(MakeVisual(raShaftMb, raMat));

            // ── RA-rotated subtree: anything that swings with HA.
            _raAxisRotation = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0.0));
            var raRotated = new ModelVisual3D { Transform = _raAxisRotation };
            polarFrame.Children.Add(raRotated);

            // Saddle — short cylinder perpendicular to RA shaft, at +Y end.
            // Lies along X within the rotated frame.
            var saddleMb = new MeshBuilder();
            saddleMb.AddCylinder(
                new Point3D(-SaddleLength / 2.0, RaShaftLength * 0.45, 0),
                new Point3D( SaddleLength / 2.0, RaShaftLength * 0.45, 0),
                SaddleRadius, 24);
            raRotated.Children.Add(MakeVisual(saddleMb, saddleMat));

            // Counterweight bar + bob, pointing along -Y (opposite the saddle).
            var ctwMb = new MeshBuilder();
            ctwMb.AddCylinder(
                new Point3D(0, -RaShaftLength * 0.45, 0),
                new Point3D(0, -RaShaftLength * 0.45 - CtwShaftLength, 0),
                CtwShaftRadius, 16);
            ctwMb.AddSphere(
                new Point3D(0, -RaShaftLength * 0.45 - CtwShaftLength + CtwBobRadius * 0.4, 0),
                CtwBobRadius, 18, 12);
            raRotated.Children.Add(MakeVisual(ctwMb, ctwMat));

            // ── DEC-rotated subtree: holds the OTA. Pivots about X (saddle axis).
            _decAxisRotation = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0.0));
            var decFrame = new ModelVisual3D
            {
                Transform = new Transform3DGroup
                {
                    Children =
                    {
                        _decAxisRotation,
                        // Move the OTA out to the +X end of the saddle so it doesn't intersect the shaft.
                        new TranslateTransform3D(SaddleLength / 2.0, RaShaftLength * 0.45, 0)
                    }
                }
            };
            raRotated.Children.Add(decFrame);

            // OTA — split into three colored sections so "front" is unambiguous:
            //   rear 70% = accent (orange),
            //   front 30% = info (cyan) shrouded dew section,
            //   front face = ok (green) objective disc.
            // Tube points along polar -Z within the dec-rotated frame: at
            // HA=0 / Dec=0 that direction maps to up-and-south in world space
            // (the celestial-equator point above the southern horizon), which
            // is where a GEM physically points when it's on the meridian.
            const double otaRearZ     =  OtaLength / 2.0;                       //  +0.5 L
            const double otaSplitZ    =  OtaLength / 2.0 - OtaLength * 0.30;    //  +0.2 L (body→front boundary)
            const double otaFrontEndZ = -OtaLength / 2.0;                       //  -0.5 L

            var otaBodyMb = new MeshBuilder();
            otaBodyMb.AddCylinder(
                new Point3D(0, 0, otaRearZ),
                new Point3D(0, 0, otaSplitZ),
                OtaRadius, 28);
            decFrame.Children.Add(MakeVisual(otaBodyMb, otaMat));

            var otaFrontMb = new MeshBuilder();
            otaFrontMb.AddCylinder(
                new Point3D(0, 0, otaSplitZ),
                new Point3D(0, 0, otaFrontEndZ),
                OtaRadius * 1.06, 28);
            decFrame.Children.Add(MakeVisual(otaFrontMb, frontMat));

            // Objective lens disc — short, fat, at the very front face. Vivid
            // accent so the front is readable from any camera angle.
            var lensMb = new MeshBuilder();
            lensMb.AddCylinder(
                new Point3D(0, 0, otaFrontEndZ + 0.005),
                new Point3D(0, 0, otaFrontEndZ - 0.025),
                OtaRadius * 1.10, 28);
            decFrame.Children.Add(MakeVisual(lensMb, lensMat));

            // Camera — front-south, slightly elevated.
            Viewport.Camera = new PerspectiveCamera
            {
                Position = new Point3D(2.4, 2.0, 3.6),
                LookDirection = new Vector3D(-2.4, -1.0, -3.6),
                UpDirection = new Vector3D(0, 1, 0),
                FieldOfView = 45
            };
        }

        private static ModelVisual3D MakeVisual(MeshBuilder mb, Material mat)
        {
            var geom = new GeometryModel3D(mb.ToMesh(), mat) { BackMaterial = mat };
            return new ModelVisual3D { Content = geom };
        }

        private static Material MakeMaterial(Color c)
        {
            var group = new MaterialGroup();
            group.Children.Add(new DiffuseMaterial(new SolidColorBrush(c)));
            // Soft specular for a hint of shape definition under DefaultLights.
            group.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)), 24));
            return group;
        }

        private Color ResolveColor(string brushKey, Color fallback)
        {
            try
            {
                if (TryFindResource(brushKey) is SolidColorBrush b) return b.Color;
                if (Application.Current?.TryFindResource(brushKey) is SolidColorBrush b2) return b2.Color;
            }
            catch { }
            return fallback;
        }
    }
}
