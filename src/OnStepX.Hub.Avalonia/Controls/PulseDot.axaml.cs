using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Controls
{
    // Only the green "connected / tracking" dot pulses, matching the WPF hub's
    // policy — a warning or error dot stays solid so it reads as a state
    // rather than as activity.
    public partial class PulseDot : UserControl
    {
        public PulseDot()
        {
            AvaloniaXamlLoader.Load(this);
            Refresh();
        }

        public static readonly StyledProperty<StatusKind> StatusProperty =
            AvaloniaProperty.Register<PulseDot, StatusKind>(nameof(Status), StatusKind.Neutral);
        public StatusKind Status
        {
            get => GetValue(StatusProperty);
            set => SetValue(StatusProperty, value);
        }

        public static readonly StyledProperty<bool> PulsingProperty =
            AvaloniaProperty.Register<PulseDot, bool>(nameof(Pulsing));
        public bool Pulsing
        {
            get => GetValue(PulsingProperty);
            set => SetValue(PulsingProperty, value);
        }

        public static readonly StyledProperty<IBrush> StatusBrushProperty =
            AvaloniaProperty.Register<PulseDot, IBrush>(nameof(StatusBrush), Brushes.Gray);
        public IBrush StatusBrush
        {
            get => GetValue(StatusBrushProperty);
            private set => SetValue(StatusBrushProperty, value);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == StatusProperty || change.Property == PulsingProperty) Refresh();
        }

        private void Refresh()
        {
            string key = Status switch
            {
                StatusKind.Ok => "Brush.Ok",
                StatusKind.Warn => "Brush.Warn",
                StatusKind.Err => "Brush.Danger",
                StatusKind.Info => "Brush.Info",
                _ => "Brush.TextFaint",
            };

            if (Application.Current != null
                && Application.Current.TryFindResource(key, out var res)
                && res is IBrush brush)
                StatusBrush = brush;

            Classes.Set("pulsing", Pulsing && Status == StatusKind.Ok);
        }
    }
}
