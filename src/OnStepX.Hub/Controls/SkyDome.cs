using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Controls
{
    // All-sky Alt/Az dome: zenith at the centre, horizon at the rim, N up / E right.
    // Plots the alignment-model points as numbered markers; clicking selects the
    // nearest one (and an empty click deselects). Code-drawn so the grid, labels
    // and markers all live in one OnRender pass.
    public sealed class SkyDome : FrameworkElement
    {
        private static readonly Typeface Face = new Typeface("Segoe UI");
        private static readonly Brush MarkerFill = Frozen(Color.FromRgb(0x3D, 0xA5, 0xFF));
        private static readonly Brush MarkerText = Brushes.White;

        // Marker centres (device px) captured at the last render, for hit-testing.
        private readonly List<Tuple<SkyModelPoint, Point>> _hits = new List<Tuple<SkyModelPoint, Point>>();

        public SkyDome()
        {
            ClipToBounds = true;
            IsVisibleChanged += (s, e) => InvalidateVisual();
        }

        public static readonly DependencyProperty SkyVMProperty = DependencyProperty.Register(
            nameof(SkyVM), typeof(SkyModelViewModel), typeof(SkyDome),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSkyVMChanged));
        public SkyModelViewModel SkyVM
        {
            get => (SkyModelViewModel)GetValue(SkyVMProperty);
            set => SetValue(SkyVMProperty, value);
        }

        private static void OnSkyVMChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var dome = (SkyDome)d;
            if (e.OldValue is SkyModelViewModel oldVm)
            {
                oldVm.Points.CollectionChanged -= dome.OnDataChanged;
                oldVm.PropertyChanged -= dome.OnVmPropertyChanged;
            }
            if (e.NewValue is SkyModelViewModel newVm)
            {
                newVm.Points.CollectionChanged += dome.OnDataChanged;
                newVm.PropertyChanged += dome.OnVmPropertyChanged;
            }
            dome.InvalidateVisual();
        }

        private void OnDataChanged(object sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SkyModelViewModel.SelectedPoint) ||
                e.PropertyName == nameof(SkyModelViewModel.PointCount) ||
                e.PropertyName == nameof(SkyModelViewModel.ModelSupported))
                InvalidateVisual();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            var vm = SkyVM;
            if (vm == null) return;

            Point click = e.GetPosition(this);
            SkyModelPoint best = null;
            double bestD2 = double.MaxValue;
            foreach (var h in _hits)
            {
                double dx = h.Item2.X - click.X, dy = h.Item2.Y - click.Y;
                double d2 = dx * dx + dy * dy;
                if (d2 < bestD2) { bestD2 = d2; best = h.Item1; }
            }
            const double thresh = 16.0;
            vm.SelectedPoint = (best != null && bestD2 <= thresh * thresh) ? best : null;
        }

        protected override void OnRender(DrawingContext dc)
        {
            _hits.Clear();
            double w = ActualWidth, h = ActualHeight;
            if (w <= 4 || h <= 4) return;

            // Transparent fill so the whole surface is hit-testable (empty click = deselect).
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

            Brush bg = FindBrush("Brush.PanelInset", Color.FromRgb(0x10, 0x13, 0x1a));
            Brush border = FindBrush("Brush.Border", Color.FromRgb(0x2a, 0x31, 0x3c));
            Brush dim = FindBrush("Brush.TextDim", Color.FromRgb(0x9a, 0xa4, 0xb2));
            Brush accent = FindBrush("Brush.Accent", Color.FromRgb(0xe5, 0x48, 0x2d));

            var gridPen = new Pen(border, 1.0); gridPen.Freeze();
            var ringPen = new Pen(border, 0.7) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) }; ringPen.Freeze();

            double margin = 22;
            double cx = w / 2, cy = h / 2;
            double R = Math.Min(w, h) / 2 - margin;
            if (R < 10) return;

            dc.DrawEllipse(bg, gridPen, new Point(cx, cy), R, R);
            dc.DrawEllipse(null, ringPen, new Point(cx, cy), R * 60.0 / 90.0, R * 60.0 / 90.0);
            dc.DrawEllipse(null, ringPen, new Point(cx, cy), R * 30.0 / 90.0, R * 30.0 / 90.0);
            dc.DrawLine(ringPen, new Point(cx, cy - R), new Point(cx, cy + R));
            dc.DrawLine(ringPen, new Point(cx - R, cy), new Point(cx + R, cy));

            DrawCentered(dc, dim, "N", cx, cy - R - 11);
            DrawCentered(dc, dim, "S", cx, cy + R + 11);
            DrawCentered(dc, dim, "E", cx + R + 11, cy);
            DrawCentered(dc, dim, "W", cx - R - 11, cy);

            var vm = SkyVM;
            if (vm == null) return;

            if (!vm.ModelSupported || vm.PointCount == 0)
            {
                DrawCentered(dc, dim, !vm.ModelSupported ? "Alignment model not supported" : "No model points", cx, cy);
                return;
            }

            foreach (var p in vm.Points)
            {
                if (double.IsNaN(p.AzDeg)) continue;
                double altClamped = double.IsNaN(p.AltDeg) ? 0 : Math.Max(0, Math.Min(90, p.AltDeg));
                double r = R * (90.0 - altClamped) / 90.0;
                double a = p.AzDeg * Math.PI / 180.0;
                double x = cx + r * Math.Sin(a);
                double y = cy - r * Math.Cos(a);
                _hits.Add(Tuple.Create(p, new Point(x, y)));

                bool sel = p.IsSelected;
                double rad = sel ? 11 : 8;
                Brush fill = sel ? accent : MarkerFill;
                Pen dotPen = sel ? new Pen(accent, 2.5) : null;
                dc.DrawEllipse(fill, dotPen, new Point(x, y), rad, rad);
                DrawCentered(dc, MarkerText, p.Number.ToString(CultureInfo.InvariantCulture), x, y, 11);
            }
        }

        private void DrawCentered(DrawingContext dc, Brush b, string s, double cx, double cy, double size = 12)
        {
            var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                Face, size, b, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
        }

        private Brush FindBrush(string key, Color fallback)
        {
            if (TryFindResource(key) is Brush res) return res;
            return Frozen(fallback);
        }

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
