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
        // Thickness/cross-section dimensions scaled 1.5× from the previous
        // pass; lengths kept the same so the mount looks chunkier without
        // changing its overall reach.
        private const double PierHeight        = 1.55;
        private const double PierShaftRadius   = 0.195;
        private const double PierBaseRadius    = 0.36;
        private const double PierBaseHeight    = 0.09;
        private const double PierTopRadius     = 0.30;
        private const double PierTopHeight     = 0.075;
        private const double PolarHousingSize  = 0.51;

        private const double RaShaftLength     = 1.05;
        private const double RaShaftRadius     = 0.1275;
        private const double RaMotorRadius     = 0.2025;
        private const double RaMotorLength     = 0.24;

        private const double SaddleLength      = 0.55;
        private const double SaddleRadius      = 0.1275;
        private const double DecMotorRadius    = 0.21;
        private const double DecMotorLength    = 0.30;

        private const double OtaLength         = 1.10;
        private const double OtaRadius         = 0.2025;
        // Extra X-axis clearance between the saddle end and the OTA centerline
        // so the (now thicker) tube doesn't clip into the saddle / DEC head.
        private const double OtaSaddleOffset   = 0.30;

        private const double PointingLineLength = 8.0;
        private const double GroundRadius       = 1.7;

        // Reference mount height for zoom clamps — pier + half the RA shaft
        // covers everything that's normally on screen.
        private const double MountRefHeight  = PierHeight + RaShaftLength * 0.5;
        private const double MaxCamDistance  = MountRefHeight * 6.0;
        private const double MinCamDistance  = MountRefHeight * 1.5;

        // --- Animation
        private static readonly Duration EaseDuration = new Duration(TimeSpan.FromMilliseconds(750));
        private static readonly IEasingFunction Easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        // --- Mutable scene transforms. These get their AxisAngleRotation3D
        // rebuilt from the proxy DP callbacks; the Helix viewport sees them
        // change and re-renders.
        private RotateTransform3D _latitudeRotation;
        private RotateTransform3D _raAxisRotation;
        private RotateTransform3D _decAxisRotation;

        // --- Initial camera state captured after BuildScene; restored on
        // double-click in the viewport.
        private Point3D  _initialCameraPos;
        private Vector3D _initialCameraLook;
        private Vector3D _initialCameraUp;
        private double   _initialCameraFov;

        // --- Zoom clamp re-entry guard.
        private bool _clampingCamera;

        // --- VM subscription
        private VisualizerViewModel _vm;

        public MountVisualizer()
        {
            InitializeComponent();
            BuildScene();
            DataContextChanged += OnDataContextChanged;
            Loaded += (_, __) => ApplyVmSnapshot();
            Unloaded += (_, __) => DetachVm();
            Viewport.MouseDoubleClick += OnViewportDoubleClick;
        }

        private void OnViewportDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (Viewport.Camera is PerspectiveCamera cam)
            {
                cam.Position      = _initialCameraPos;
                cam.LookDirection = _initialCameraLook;
                cam.UpDirection   = _initialCameraUp;
                cam.FieldOfView   = _initialCameraFov;
            }
            e.Handled = true;
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

            var pierColor    = ResolveColor("Brush.BorderStrong", Color.FromRgb(0x3a, 0x42, 0x50));
            var housingColor = ResolveColor("Brush.Border",       Color.FromRgb(0x2a, 0x31, 0x3c));
            var metalColor   = ResolveColor("Brush.TextDim",      Color.FromRgb(0x9a, 0xa4, 0xb2));
            var otaColor     = ResolveColor("Brush.Accent",       Color.FromRgb(0xe5, 0x48, 0x2d));
            var frontColor   = ResolveColor("Brush.Info",         Color.FromRgb(0x5f, 0x9e, 0xd4));
            var lensColor    = ResolveColor("Brush.Ok",           Color.FromRgb(0x4a, 0xc2, 0x7a));
            var groundColor  = ResolveColor("Brush.Panel",        Color.FromRgb(0x1b, 0x1f, 0x26));
            var gridColor    = ResolveColor("Brush.TextFaint",    Color.FromRgb(0x6b, 0x75, 0x82));

            var pierMat    = MakeMaterial(pierColor,    softSpec: true);
            var housingMat = MakeMaterial(housingColor, softSpec: true);
            var metalMat   = MakeMaterial(metalColor,   softSpec: true);
            var otaMat     = MakeMaterial(otaColor,     softSpec: true);
            var frontMat   = MakeMaterial(frontColor,   softSpec: true);
            var lensMat    = MakeLensMaterial(lensColor);
            var groundMat  = MakeMaterial(groundColor,  softSpec: false);

            // ── Ground: solid disc at y=0 plus a square wireframe grid on top.
            var groundMb = new MeshBuilder();
            groundMb.AddCylinder(new Point3D(0, -0.005, 0), new Point3D(0, 0.0, 0), GroundRadius, 64);
            Viewport.Children.Add(MakeVisual(groundMb, groundMat));

            Viewport.Children.Add(new GridLinesVisual3D
            {
                Center = new Point3D(0, 0.001, 0),
                Length = GroundRadius * 2.0,
                Width  = GroundRadius * 2.0,
                LengthDirection = new Vector3D(0, 0, 1),
                Normal = new Vector3D(0, 1, 0),
                MajorDistance = 0.5,
                MinorDistance = 0.1,
                Thickness = 0.0035,
                Fill = new SolidColorBrush(Color.FromArgb(140, gridColor.R, gridColor.G, gridColor.B))
            });

            // ── Pier: base flange + cylindrical shaft + top flange.
            var pierMb = new MeshBuilder();
            pierMb.AddCylinder(new Point3D(0, 0,             0), new Point3D(0, PierBaseHeight,             0), PierBaseRadius, 36);
            pierMb.AddCylinder(new Point3D(0, PierBaseHeight, 0), new Point3D(0, PierHeight - PierTopHeight, 0), PierShaftRadius, 36);
            pierMb.AddCylinder(new Point3D(0, PierHeight - PierTopHeight, 0), new Point3D(0, PierHeight, 0), PierTopRadius, 36);
            Viewport.Children.Add(MakeVisual(pierMb, pierMat));

            // ── Polar frame: tilts so its +Y axis points at the celestial pole.
            _latitudeRotation = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 45.0));
            UpdateLatitudeRotation();
            var polarTransform = new Transform3DGroup();
            polarTransform.Children.Add(_latitudeRotation);
            polarTransform.Children.Add(new TranslateTransform3D(0, PierHeight, 0));
            var polarFrame = new ModelVisual3D { Transform = polarTransform };
            Viewport.Children.Add(polarFrame);

            // Polar housing — fixed (does NOT rotate with HA). The chunk that
            // sits on top of the pier and houses the RA bearing.
            var polarHousingMb = new MeshBuilder();
            polarHousingMb.AddBox(new Point3D(0, 0, 0), PolarHousingSize, PolarHousingSize * 0.55, PolarHousingSize);
            polarFrame.Children.Add(MakeVisual(polarHousingMb, housingMat));

            // ── RA-rotated subtree: shaft + saddle + DEC head + counterweight
            //    all swing with HA.
            _raAxisRotation = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0.0));
            var raRotated = new ModelVisual3D { Transform = _raAxisRotation };
            polarFrame.Children.Add(raRotated);

            // RA shaft (rotates with HA, runs through the polar housing).
            var raShaftMb = new MeshBuilder();
            raShaftMb.AddCylinder(
                new Point3D(0, -RaShaftLength * 0.5, 0),
                new Point3D(0,  RaShaftLength * 0.5, 0),
                RaShaftRadius, 28);
            // RA motor cap at the counterweight end of the shaft.
            raShaftMb.AddCylinder(
                new Point3D(0, -RaShaftLength * 0.5,                    0),
                new Point3D(0, -RaShaftLength * 0.5 - RaMotorLength,    0),
                RaMotorRadius, 32);
            raRotated.Children.Add(MakeVisual(raShaftMb, housingMat));

            // ── Saddle / DEC head assembly (in raRotated, lies along X at +Y end).
            // Saddle bar — extends past +X end of nominal saddle so it reaches
            // the (offset) OTA centerline. -X end stays where the DEC motor
            // attaches.
            var saddleMb = new MeshBuilder();
            saddleMb.AddCylinder(
                new Point3D(-SaddleLength / 2.0,                       RaShaftLength * 0.5, 0),
                new Point3D( SaddleLength / 2.0 + OtaSaddleOffset,     RaShaftLength * 0.5, 0),
                SaddleRadius, 28);
            raRotated.Children.Add(MakeVisual(saddleMb, metalMat));

            // DEC motor housing — chunky cylinder at the -X end of the saddle
            // (opposite the OTA mount). Stays with raRotated; doesn't pivot
            // with Dec.
            var decMotorMb = new MeshBuilder();
            decMotorMb.AddCylinder(
                new Point3D(-SaddleLength / 2.0,                  RaShaftLength * 0.5, 0),
                new Point3D(-SaddleLength / 2.0 - DecMotorLength, RaShaftLength * 0.5, 0),
                DecMotorRadius, 32);
            raRotated.Children.Add(MakeVisual(decMotorMb, housingMat));

            // ── DEC-rotated subtree: holds the OTA.
            _decAxisRotation = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0.0));
            var decFrame = new ModelVisual3D
            {
                Transform = new Transform3DGroup
                {
                    Children =
                    {
                        _decAxisRotation,
                        // OTA mount sits past the +X end of the saddle —
                        // offset out far enough that the tube doesn't clip
                        // into the saddle / DEC head after thickening.
                        new TranslateTransform3D(SaddleLength / 2.0 + OtaSaddleOffset, RaShaftLength * 0.5, 0)
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
            // (the celestial equator point above the southern horizon).
            const double otaRearZ     =  OtaLength / 2.0;
            const double otaSplitZ    =  OtaLength / 2.0 - OtaLength * 0.30;
            const double otaFrontEndZ = -OtaLength / 2.0;

            var otaBodyMb = new MeshBuilder();
            otaBodyMb.AddCylinder(new Point3D(0, 0, otaRearZ), new Point3D(0, 0, otaSplitZ), OtaRadius, 32);
            decFrame.Children.Add(MakeVisual(otaBodyMb, otaMat));

            var otaFrontMb = new MeshBuilder();
            otaFrontMb.AddCylinder(new Point3D(0, 0, otaSplitZ), new Point3D(0, 0, otaFrontEndZ), OtaRadius * 1.06, 32);
            decFrame.Children.Add(MakeVisual(otaFrontMb, frontMat));

            // Objective lens disc — short, fat, at the very front face.
            var lensMb = new MeshBuilder();
            lensMb.AddCylinder(
                new Point3D(0, 0, otaFrontEndZ + 0.005),
                new Point3D(0, 0, otaFrontEndZ - 0.025),
                OtaRadius * 1.10, 32);
            decFrame.Children.Add(MakeVisual(lensMb, lensMat));

            // Pointing line — long thin line extending from the front of the
            // objective straight along the OTA's optical axis, into "the sky."
            // Helps locate where the scope is pointing from any camera angle.
            var pointingLine = new LinesVisual3D
            {
                Color = lensColor,
                Thickness = 1.4,
                Points = new Point3DCollection
                {
                    new Point3D(0, 0, otaFrontEndZ - 0.04),
                    new Point3D(0, 0, otaFrontEndZ - PointingLineLength)
                }
            };
            decFrame.Children.Add(pointingLine);

            // ── Camera — front-south, slightly elevated. Saved for double-click reset.
            var camera = new PerspectiveCamera
            {
                Position      = new Point3D(2.6, 2.1, 3.8),
                LookDirection = new Vector3D(-2.6, -1.2, -3.8),
                UpDirection   = new Vector3D(0, 1, 0),
                FieldOfView   = 45
            };
            Viewport.Camera = camera;
            _initialCameraPos  = camera.Position;
            _initialCameraLook = camera.LookDirection;
            _initialCameraUp   = camera.UpDirection;
            _initialCameraFov  = camera.FieldOfView;

            // Zoom clamp: react to any camera Position change (mouse wheel,
            // pan, double-click reset, etc.) and snap distance from world
            // origin back into [MinCamDistance, MaxCamDistance].
            System.ComponentModel.DependencyPropertyDescriptor
                .FromProperty(PerspectiveCamera.PositionProperty, typeof(PerspectiveCamera))
                .AddValueChanged(camera, OnCameraPositionChanged);
        }

        private void OnCameraPositionChanged(object sender, EventArgs e)
        {
            if (_clampingCamera) return;
            if (!(Viewport.Camera is PerspectiveCamera cam)) return;
            var p = cam.Position;
            double dist = Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
            if (dist <= 0) return;
            double clamped = Math.Max(MinCamDistance, Math.Min(MaxCamDistance, dist));
            if (Math.Abs(clamped - dist) < 1e-6) return;
            _clampingCamera = true;
            try
            {
                double s = clamped / dist;
                cam.Position = new Point3D(p.X * s, p.Y * s, p.Z * s);
            }
            finally { _clampingCamera = false; }
        }

        private static ModelVisual3D MakeVisual(MeshBuilder mb, Material mat)
        {
            var geom = new GeometryModel3D(mb.ToMesh(), mat) { BackMaterial = mat };
            return new ModelVisual3D { Content = geom };
        }

        private static Material MakeMaterial(Color c, bool softSpec = true)
        {
            var group = new MaterialGroup();
            group.Children.Add(new DiffuseMaterial(new SolidColorBrush(c)));
            if (softSpec)
            {
                // Specular hint for shape definition under DefaultLights — gives
                // the dull-metal appearance on housings and the painted finish on
                // the OTA without going full chrome.
                group.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 32));
            }
            return group;
        }

        private static Material MakeLensMaterial(Color c)
        {
            // Lens face: diffuse + emissive so the green objective glows from
            // any camera angle, plus a hot specular highlight for a "glassy"
            // glint.
            var group = new MaterialGroup();
            group.Children.Add(new DiffuseMaterial(new SolidColorBrush(c)));
            group.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(110, c.R, c.G, c.B))));
            group.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), 80));
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
