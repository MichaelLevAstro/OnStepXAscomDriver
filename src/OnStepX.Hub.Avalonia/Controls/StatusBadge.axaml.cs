using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Controls
{
    public partial class StatusBadge : UserControl
    {
        public StatusBadge() => AvaloniaXamlLoader.Load(this);

        public static readonly StyledProperty<StatusKind> StatusProperty =
            AvaloniaProperty.Register<StatusBadge, StatusKind>(nameof(Status), StatusKind.Neutral);
        public StatusKind Status
        {
            get => GetValue(StatusProperty);
            set => SetValue(StatusProperty, value);
        }

        public static readonly StyledProperty<bool> PulsingProperty =
            AvaloniaProperty.Register<StatusBadge, bool>(nameof(Pulsing));
        public bool Pulsing
        {
            get => GetValue(PulsingProperty);
            set => SetValue(PulsingProperty, value);
        }

        public static readonly StyledProperty<string> TextProperty =
            AvaloniaProperty.Register<StatusBadge, string>(nameof(Text), "");
        public string Text
        {
            get => GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public static readonly StyledProperty<IBrush> TextBrushProperty =
            AvaloniaProperty.Register<StatusBadge, IBrush>(nameof(TextBrush), Brushes.Gainsboro);
        public IBrush TextBrush
        {
            get => GetValue(TextBrushProperty);
            set => SetValue(TextBrushProperty, value);
        }
    }
}
