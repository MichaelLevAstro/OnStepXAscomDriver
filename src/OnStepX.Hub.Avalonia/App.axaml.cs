using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ASCOM.OnStepX.Services;
using ASCOM.OnStepX.ViewModels;
using ASCOM.OnStepX.Views;

namespace ASCOM.OnStepX
{
    public partial class App : Application
    {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Themes before the first window, so nothing flashes in the
                // wrong palette while the window materialises.
                ThemeService.Initialise(this);

                var vm = new MainViewModel();
                desktop.MainWindow = new MainWindow { DataContext = vm };

                // The hub lives in the tray on Windows and keeps running with
                // no window on a headless Pi, so closing the last window must
                // not end the process. Program decides when to exit.
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
