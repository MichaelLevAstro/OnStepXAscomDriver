using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ASCOM.OnStepX.Services;

namespace ASCOM.OnStepX.Controls
{
    public partial class ThemeToggle : UserControl
    {
        public ThemeToggle()
        {
            InitializeComponent();
            ThemeService.Changed += (s, e) => RefreshActive();
            Loaded += (s, e) => RefreshActive();
        }

        private void OnDark(object sender, RoutedEventArgs e)  => ThemeService.SetMode(ThemeMode.Dark);
        private void OnLight(object sender, RoutedEventArgs e) => ThemeService.SetMode(ThemeMode.Light);

        private void RefreshActive()
        {
            // Active button gets the active button background; inactive stays
            // transparent. Foreground flips from dim → text on active.
            var activeBg = (Brush)(TryFindResource("Brush.BtnBgActive") ?? Brushes.Gray);
            var activeFg = (Brush)(TryFindResource("Brush.Text") ?? Brushes.White);
            var inactiveFg = (Brush)(TryFindResource("Brush.TextDim") ?? Brushes.Gray);

            bool dark = ThemeService.Mode == ThemeMode.Dark;
            DarkBtn.Background  = dark ? activeBg : Brushes.Transparent;
            DarkBtn.Foreground  = dark ? activeFg : inactiveFg;
            LightBtn.Background = !dark ? activeBg : Brushes.Transparent;
            LightBtn.Foreground = !dark ? activeFg : inactiveFg;
        }
    }
}
