using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using ASCOM.OnStepX.Hardware.Transport;
using ASCOM.OnStepX.Platform;

namespace ASCOM.OnStepX.Hub.Platform
{
    // ISettingsStore as a JSON file, so the hub keeps its settings somewhere
    // that exists on every platform.
    //
    //   Windows : %AppData%\OnStepX\settings.json
    //   Linux   : $XDG_CONFIG_HOME/onstepx/settings.json, else ~/.config/onstepx
    //   macOS   : ~/.config/onstepx  (same as Linux; the hub is not a bundle-
    //             style app and this keeps one path to document)
    //
    // On Windows, the first run with no file imports every value out of
    // HKCU\Software\ASCOM\OnStepX — where the WPF hub has always kept them —
    // so an existing install carries its COM port, site, limits and rates over
    // instead of starting blank. The registry is left untouched: the WPF hub
    // is still the shipping one and has to keep working.
    //
    // Nothing here throws. Settings are not worth taking the hub down for.
    internal sealed class JsonSettingsStore : ISettingsStore
    {
        private const string RegPath = @"Software\ASCOM\OnStepX";

        private readonly object _gate = new();
        private readonly string _path;
        private Dictionary<string, string> _values;

        public JsonSettingsStore()
        {
            _path = Path.Combine(ConfigDirectory(), "settings.json");
            _values = Load();
        }

        public string Read(string name, string fallback)
        {
            if (string.IsNullOrEmpty(name)) return fallback;
            lock (_gate)
                return _values.TryGetValue(name, out var v) ? v : fallback;
        }

        public void Write(string name, string value)
        {
            if (string.IsNullOrEmpty(name)) return;
            lock (_gate)
            {
                if (_values.TryGetValue(name, out var existing) && existing == (value ?? "")) return;
                _values[name] = value ?? "";
                Save();
            }
        }

        public string FilePath => _path;

        // ---- storage --------------------------------------------------------

        private static string ConfigDirectory()
        {
            string dir;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OnStepX");
            }
            else
            {
                var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (string.IsNullOrEmpty(xdg))
                    xdg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
                dir = Path.Combine(xdg, "onstepx");
            }

            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }

        private Dictionary<string, string> Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    var json = File.ReadAllText(_path);
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (parsed != null)
                        return new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                try { TransportLogger.Note("Settings file unreadable, starting empty: " + ex.Message); } catch { }
            }

            var imported = ImportFromRegistry();
            if (imported.Count > 0)
            {
                _values = imported;
                try
                {
                    Save();
                    TransportLogger.Note("Imported " + imported.Count + " settings from HKCU into " + _path);
                }
                catch { }
            }
            return imported;
        }

        private void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true });
                // Write beside the target and move into place, so a crash
                // mid-write cannot leave a half-written settings file.
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(tmp, _path);
            }
            catch (Exception ex)
            {
                try { TransportLogger.Note("Could not save settings: " + ex.Message); } catch { }
            }
        }

        private static Dictionary<string, string> ImportFromRegistry()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return map;

            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegPath);
                if (key == null) return map;
                foreach (var name in key.GetValueNames())
                {
                    var v = key.GetValue(name);
                    if (v != null) map[name] = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                }
            }
            catch { }

            return map;
        }
    }
}
