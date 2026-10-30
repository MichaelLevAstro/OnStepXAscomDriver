using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Controls
{
    public partial class SlewPad : UserControl
    {
        private string _activeDir;

        public SlewPad() => AvaloniaXamlLoader.Load(this);

        public static readonly StyledProperty<ParkHomeViewModel> PadVMProperty =
            AvaloniaProperty.Register<SlewPad, ParkHomeViewModel>(nameof(PadVM));
        public ParkHomeViewModel PadVM
        {
            get => GetValue(PadVMProperty);
            set => SetValue(PadVMProperty, value);
        }

        public static readonly StyledProperty<bool> IsArmedProperty =
            AvaloniaProperty.Register<SlewPad, bool>(nameof(IsArmed), true);
        public bool IsArmed
        {
            get => GetValue(IsArmedProperty);
            set => SetValue(IsArmedProperty, value);
        }

        private void OnPress(object sender, PointerPressedEventArgs e)
        {
            if (!IsArmed || PadVM == null) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (sender is not Button b || b.Tag is not string dir) return;

            _activeDir = dir;
            PadVM.BeginSlew(dir);
        }

        // Also wired to PointerExited: a button that is pressed and then
        // dragged off would otherwise never see the release, and the mount
        // would keep moving.
        private void OnRelease(object sender, RoutedEventArgs e)
        {
            if (PadVM == null || _activeDir == null) return;
            PadVM.EndSlew(_activeDir);
            _activeDir = null;
        }

        private void OnStop(object sender, RoutedEventArgs e)
        {
            if (PadVM == null) return;
            _activeDir = null;
            PadVM.StopCommand?.Execute(null);
        }
    }
}
