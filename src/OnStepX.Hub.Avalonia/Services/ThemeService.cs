using System;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using ASCOM.OnStepX.Config;

namespace ASCOM.OnStepX.Services
{
    public enum ThemeMode { Dark, Light }

    // Avalonia port of the WPF hub's ThemeService. Same contract: one theme
    // slot in the application's resources, swapped at runtime, with every
    // brush consumed through DynamicResource so the whole UI recolours in one
    // pass. The choice persists through DriverSettings.Theme exactly as before.
    public static class ThemeService
    {
        public static event EventHandler Changed;

        private const string DarkUri = "avares://OnStepX.Hub.Avalonia/Resources/Themes/Dark.axaml";
        private const string LightUri = "avares://OnStepX.Hub.Avalonia/Resources/Themes/Light.axaml";

        public static ThemeMode Mode { get; private set; }

        private static Application _app;
        private static int _slot = -1;

        public static void Initialise(Application app)
        {
            _app = app;

            // Reserve the slot now so Apply always replaces rather than
            // appends — otherwise every toggle would grow the dictionary list.
            _slot = _app.Resources.MergedDictionaries.Count;
            _app.Resources.MergedDictionaries.Add(Load(DarkUri));

            string saved;
            try { saved = (DriverSettings.Theme ?? "dark").Trim().ToLowerInvariant(); }
            catch { saved = "dark"; }
            Apply(saved == "light" ? ThemeMode.Light : ThemeMode.Dark, persist: false);
        }

        public static void SetMode(ThemeMode mode) => Apply(mode, persist: true);

        public static void Toggle() => SetMode(Mode == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark);

        private static void Apply(ThemeMode mode, bool persist)
        {
            Mode = mode;

            if (_app != null && _slot >= 0 && _slot < _app.Resources.MergedDictionaries.Count)
                _app.Resources.MergedDictionaries[_slot] = Load(mode == ThemeMode.Dark ? DarkUri : LightUri);

            if (persist)
            {
                try { DriverSettings.Theme = mode == ThemeMode.Dark ? "dark" : "light"; } catch { }
            }

            try { Changed?.Invoke(null, EventArgs.Empty); } catch { }
        }

        private static ResourceInclude Load(string uri) =>
            new ResourceInclude(new Uri(uri)) { Source = new Uri(uri) };
    }
}
