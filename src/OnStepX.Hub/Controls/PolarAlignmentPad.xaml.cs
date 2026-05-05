using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Controls
{
    // 5-button plus pad. Each direction button carries
    // "<focuser>,<dirSign>" as CommandParameter; speed comes from the
    // PolarAlignmentViewModel.SelectedSpeed dropdown so users don't have
    // to pick speed every click.
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
            if (parts.Length != 2) return;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int axis)) return;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int dir)) return;
            PadVM.Jog(axis, dir, PadVM.SelectedSpeed);
        }

        private void OnStopAll(object sender, RoutedEventArgs e)
        {
            PadVM?.StopAllCommand?.Execute(null);
        }
    }
}
