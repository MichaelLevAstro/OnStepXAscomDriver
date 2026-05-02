using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Controls
{
    // PulseDot — connected/error/warn/info indicator with optional pulsing
    // halo. Mirrors the WinForms PulseDot custom control. The halo storyboard
    // runs only when both Pulsing=true and Status=Ok (matches HubForm's policy
    // — only the green tracking/connected dot pulses).
    public partial class PulseDot : UserControl
    {
        private Storyboard _pulse;

        public PulseDot()
        {
            InitializeComponent();
            BuildPulseStoryboard();
            Loaded += (s, e) => UpdateStoryboard();
            Unloaded += (s, e) => StopPulse();
        }

        public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
            nameof(Status), typeof(StatusKind), typeof(PulseDot),
            new PropertyMetadata(StatusKind.Neutral, OnStatusOrPulseChanged));

        public StatusKind Status { get => (StatusKind)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }

        public static readonly DependencyProperty PulsingProperty = DependencyProperty.Register(
            nameof(Pulsing), typeof(bool), typeof(PulseDot),
            new PropertyMetadata(false, OnStatusOrPulseChanged));

        public bool Pulsing { get => (bool)GetValue(PulsingProperty); set => SetValue(PulsingProperty, value); }

        public static readonly DependencyProperty StatusBrushProperty = DependencyProperty.Register(
            nameof(StatusBrush), typeof(Brush), typeof(PulseDot),
            new PropertyMetadata(Brushes.Gray));

        public Brush StatusBrush { get => (Brush)GetValue(StatusBrushProperty); private set => SetValue(StatusBrushProperty, value); }

        private static void OnStatusOrPulseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((PulseDot)d).RefreshBrush();
            ((PulseDot)d).UpdateStoryboard();
        }

        private void RefreshBrush()
        {
            string key;
            switch (Status)
            {
                case StatusKind.Ok:    key = "Brush.Ok"; break;
                case StatusKind.Warn:  key = "Brush.Warn"; break;
                case StatusKind.Err:   key = "Brush.Danger"; break;
                case StatusKind.Info:  key = "Brush.Info"; break;
                default:               key = "Brush.TextFaint"; break;
            }
            StatusBrush = (Brush)(TryFindResource(key) ?? Brushes.Gray);
        }

        private void BuildPulseStoryboard()
        {
            // Halo grows from 1.0x to 1.4x while fading 0.45 → 0; 1.6 s loop.
            // Peak diameter = 14*1.4 = ~20px, fits the 20x20 host without
            // clipping against the section card's rounded border. Matches the
            // CSS reference (3px → 6px box-shadow halo on an 8px dot).
            _pulse = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

            var sx = new DoubleAnimation(1.0, 1.4, TimeSpan.FromSeconds(1.6))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(sx, Halo);
            Storyboard.SetTargetProperty(sx, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
            _pulse.Children.Add(sx);

            var sy = new DoubleAnimation(1.0, 1.4, TimeSpan.FromSeconds(1.6))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(sy, Halo);
            Storyboard.SetTargetProperty(sy, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
            _pulse.Children.Add(sy);

            var op = new DoubleAnimation(0.45, 0.0, TimeSpan.FromSeconds(1.6));
            Storyboard.SetTarget(op, Halo);
            Storyboard.SetTargetProperty(op, new PropertyPath(OpacityProperty));
            _pulse.Children.Add(op);
        }

        private void UpdateStoryboard()
        {
            bool shouldPulse = Pulsing && (Status == StatusKind.Ok || Status == StatusKind.Info);
            if (shouldPulse) _pulse?.Begin(this, true);
            else StopPulse();
        }

        private void StopPulse()
        {
            try { _pulse?.Stop(this); } catch { }
            Halo.Opacity = 0.25;
            HaloScale.ScaleX = 1.0;
            HaloScale.ScaleY = 1.0;
        }
    }
}
