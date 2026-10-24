using System.Windows.Controls;
using System.Windows.Input;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Views.Tabs
{
    public partial class MainTab : UserControl
    {
        public MainTab() { InitializeComponent(); }

        // Spiral search button. Press/release semantics match SlewPad's
        // direction buttons — capture so MouseLeave fires reliably and the
        // spiral stops if the user drags off the button.
        private ParkHomeViewModel Pad => (DataContext as MainViewModel)?.ParkHome;

        private void OnSpiralPress(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is Button btn) || Pad == null) return;
            btn.CaptureMouse();
            Pad.BeginSpiral();
            e.Handled = true;
        }

        private void OnSpiralRelease(object sender, MouseEventArgs e)
        {
            if (sender is Button btn && btn.IsMouseCaptured) btn.ReleaseMouseCapture();
            Pad?.EndSpiral();
        }
    }
}
