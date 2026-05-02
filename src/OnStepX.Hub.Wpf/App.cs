using System;
using System.Windows;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Services;

namespace ASCOM.OnStepX
{
    // Code-only Application subclass. Avoids the auto-generated Main from
    // App.xaml so our [STAThread] entry point in Program.cs stays in charge
    // of mutex / tray / pipe server lifecycle (mirrors the legacy hub).
    public sealed class App : Application
    {
        public App()
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            LoadGlobalResources();
            ThemeService.Initialise(this);
        }

        private void LoadGlobalResources()
        {
            // Theme dictionaries own all SolidColorBrush keys (Brush.Bg, Brush.Accent,
            // etc.). ThemeService swaps Dark<->Light at runtime; DynamicResource
            // bindings throughout the XAML re-resolve and the UI recolors live.
            Add("/OnStepX.Hub.Wpf;component/Resources/Typography.xaml");
            Add("/OnStepX.Hub.Wpf;component/Resources/Icons.xaml");
            Add("/OnStepX.Hub.Wpf;component/Resources/Controls.xaml");
        }

        private void Add(string pack)
        {
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(pack, UriKind.Relative)
            });
        }
    }
}
