using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Controls
{
    // Manual slew pad. Press = mount move in direction(s); release / mouse-leave
    // = stop the corresponding axes. Mirrors the WinForms SlewPadControl press/
    // release semantics so the wire commands sequence the same way.
    public partial class SlewPad : UserControl
    {
        public SlewPad() { InitializeComponent(); }

        public static readonly DependencyProperty PadVMProperty = DependencyProperty.Register(
            nameof(PadVM), typeof(ParkHomeViewModel), typeof(SlewPad), new PropertyMetadata(null));
        public ParkHomeViewModel PadVM { get => (ParkHomeViewModel)GetValue(PadVMProperty); set => SetValue(PadVMProperty, value); }

        public static readonly DependencyProperty IsArmedProperty = DependencyProperty.Register(
            nameof(IsArmed), typeof(bool), typeof(SlewPad), new PropertyMetadata(true));
        public bool IsArmed { get => (bool)GetValue(IsArmedProperty); set => SetValue(IsArmedProperty, value); }

        private string _activeDir;

        private void OnPress(object sender, MouseButtonEventArgs e)
        {
            if (!IsArmed || PadVM == null) return;
            var btn = (Button)sender;
            string dir = (string)btn.CommandParameter;
            if (string.IsNullOrEmpty(dir)) return;
            // Capture so MouseLeave fires reliably and we can stop the axis if
            // the user drags off the button.
            btn.CaptureMouse();
            _activeDir = dir;
            PadVM.BeginSlew(dir);
            e.Handled = true;
        }

        private void OnRelease(object sender, MouseEventArgs e)
        {
            if (PadVM == null) return;
            if (sender is Button btn && btn.IsMouseCaptured) btn.ReleaseMouseCapture();
            if (string.IsNullOrEmpty(_activeDir)) return;
            PadVM.EndSlew(_activeDir);
            _activeDir = null;
        }

        private void OnStop(object sender, RoutedEventArgs e)
        {
            if (PadVM == null) return;
            PadVM.StopCommand?.Execute(null);
        }
    }
}
