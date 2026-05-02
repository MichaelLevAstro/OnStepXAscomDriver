using System;
using System.Linq;
using System.Windows;
using ASCOM.OnStepX.Config;

namespace ASCOM.OnStepX.Services
{
    public enum ThemeMode { Dark, Light }

    // WPF replacement for Ui/Theming/Theme.cs. Owns the theme slot in
    // App.Resources.MergedDictionaries — everything else binds via
    // DynamicResource so a SetMode call recolors the entire UI in one pass,
    // including any open child windows. Persists to DriverSettings.Theme so
    // the choice survives restart and matches the legacy hub's preference.
    public static class ThemeService
    {
        public static event EventHandler Changed;

        private const string DarkUri = "/OnStepX.Hub;component/Resources/Themes/Dark.xaml";
        private const string LightUri = "/OnStepX.Hub;component/Resources/Themes/Light.xaml";

        public static ThemeMode Mode { get; private set; }

        private static Application _app;

        public static void Initialise(Application app)
        {
            _app = app;
            string saved;
            try { saved = (DriverSettings.Theme ?? "dark").Trim().ToLowerInvariant(); }
            catch { saved = "dark"; }
            ApplyMode(saved == "light" ? ThemeMode.Light : ThemeMode.Dark, persist: false);
        }

        public static void SetMode(ThemeMode mode) => ApplyMode(mode, persist: true);

        public static void Toggle() =>
            SetMode(Mode == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark);

        private static void ApplyMode(ThemeMode mode, bool persist)
        {
            if (_app == null) return;

            var newUri = new Uri(mode == ThemeMode.Dark ? DarkUri : LightUri, UriKind.Relative);
            var newDict = new ResourceDictionary { Source = newUri };

            // Drop any previously merged theme dictionary, then insert at index 0
            // so theme keys take precedence over later (semantic) dictionaries
            // that may be added in the future.
            var existing = _app.Resources.MergedDictionaries
                .Where(d => d.Source != null
                         && (d.Source.OriginalString.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase)
                          || d.Source.OriginalString.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase)))
                .ToList();
            foreach (var d in existing) _app.Resources.MergedDictionaries.Remove(d);
            _app.Resources.MergedDictionaries.Insert(0, newDict);

            Mode = mode;
            if (persist)
            {
                try { DriverSettings.Theme = mode == ThemeMode.Dark ? "dark" : "light"; } catch { }
            }
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
