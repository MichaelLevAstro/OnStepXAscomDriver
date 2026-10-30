using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Views.Tabs
{
    public partial class MainTab : UserControl
    {
        private bool _spiralRunning;

        public MainTab() => AvaloniaXamlLoader.Load(this);

        private ParkHomeViewModel Pad => (DataContext as MainViewModel)?.ParkHome;

        // Hold to spiral, release to stop. PointerExited counts as a release
        // for the same reason it does on the slew pad: a press dragged off the
        // button would otherwise leave the mount running.
        private void OnSpiralPress(object sender, PointerPressedEventArgs e)
        {
            if (Pad == null) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

            _spiralRunning = true;
            Pad.BeginSpiral();
            e.Handled = true;
        }

        private void OnSpiralRelease(object sender, RoutedEventArgs e)
        {
            if (!_spiralRunning) return;
            _spiralRunning = false;
            Pad?.EndSpiral();
        }
    }
}
