using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Controls
{
    // PulseDot + label combo. Mirrors WinForms StatusLabel.
    public partial class StatusBadge : UserControl
    {
        public StatusBadge() { InitializeComponent(); RefreshTextBrush(); }

        public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
            nameof(Status), typeof(StatusKind), typeof(StatusBadge),
            new PropertyMetadata(StatusKind.Neutral, (d, e) => ((StatusBadge)d).RefreshTextBrush()));
        public StatusKind Status { get => (StatusKind)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }

        public static readonly DependencyProperty PulsingProperty = DependencyProperty.Register(
            nameof(Pulsing), typeof(bool), typeof(StatusBadge), new PropertyMetadata(false));
        public bool Pulsing { get => (bool)GetValue(PulsingProperty); set => SetValue(PulsingProperty, value); }

        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(StatusBadge), new PropertyMetadata(""));
        public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

        public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
            nameof(TextBrush), typeof(Brush), typeof(StatusBadge),
            new PropertyMetadata(Brushes.Gray));
        public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); private set => SetValue(TextBrushProperty, value); }

        private void RefreshTextBrush()
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
            TextBrush = (Brush)(TryFindResource(key) ?? Brushes.Gray);
        }
    }
}
