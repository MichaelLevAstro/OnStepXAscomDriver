using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Controls
{
    public partial class PolarAlignmentPad : UserControl
    {
        public PolarAlignmentPad() => AvaloniaXamlLoader.Load(this);

        public static readonly StyledProperty<PolarAlignmentViewModel> PadVMProperty =
            AvaloniaProperty.Register<PolarAlignmentPad, PolarAlignmentViewModel>(nameof(PadVM));
        public PolarAlignmentViewModel PadVM
        {
            get => GetValue(PadVMProperty);
            set => SetValue(PadVMProperty, value);
        }

        // "axis,direction" — the WPF version carried it in CommandParameter,
        // which here would collide with the Click handler, so it rides on Tag.
        private void OnJog(object sender, RoutedEventArgs e)
        {
            if (PadVM == null) return;
            if (sender is not Button btn || btn.Tag is not string raw || raw.Length == 0) return;

            var parts = raw.Split(',');
            if (parts.Length != 2) return;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int axis)) return;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int dir)) return;

            PadVM.Jog(axis, dir, PadVM.SelectedSpeed);
        }

        private void OnStopAll(object sender, RoutedEventArgs e)
            => PadVM?.StopAllCommand?.Execute(null);
    }
}
