using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Controls
{
    // Click-to-step pad for the Polar Alignment Wedge panel. Each non-STOP
    // button carries a "axis,dir,speed" CommandParameter; OnJog parses and
    // dispatches to PolarAlignmentViewModel.Jog. STOP halts both axes.
    public partial class PolarAlignmentPad : UserControl
    {
        public PolarAlignmentPad() { InitializeComponent(); }

        public static readonly DependencyProperty PadVMProperty = DependencyProperty.Register(
            nameof(PadVM), typeof(PolarAlignmentViewModel), typeof(PolarAlignmentPad), new PropertyMetadata(null));
        public PolarAlignmentViewModel PadVM { get => (PolarAlignmentViewModel)GetValue(PadVMProperty); set => SetValue(PadVMProperty, value); }

        private void OnJog(object sender, RoutedEventArgs e)
        {
            if (PadVM == null) return;
            var btn = sender as Button;
            string raw = btn?.CommandParameter as string;
            if (string.IsNullOrEmpty(raw)) return;
            var parts = raw.Split(',');
            if (parts.Length != 3) return;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int axis)) return;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int dir)) return;
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int speed)) return;
            PadVM.Jog(axis, dir, speed);
        }

        private void OnStopAll(object sender, RoutedEventArgs e)
        {
            PadVM?.StopAllCommand?.Execute(null);
        }
    }
}
