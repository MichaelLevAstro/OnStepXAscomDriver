using System;
using System.Globalization;
using ASCOM.OnStepX.Platform;
using Microsoft.Win32;

namespace ASCOM.OnStepX.Hub.Platform
{
    // HKCU\Software\ASCOM\OnStepX — where every hub setting has always lived
    // and where every existing install's settings still are, so this keeps
    // reading and writing exactly that.
    //
    // DriverSettings spelled the path @"Software\\ASCOM\\OnStepX" with doubled
    // separators; Windows collapses the empty path components, so it resolved
    // to the same key DebugLogger used with single ones. Written single here,
    // which is what is actually in the registry.
    internal sealed class RegistrySettingsStore : ISettingsStore
    {
        private const string RegPath = @"Software\ASCOM\OnStepX";

        public string Read(string name, string fallback)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RegPath))
                {
                    var v = k?.GetValue(name);
                    if (v == null) return fallback;
                    return Convert.ToString(v, CultureInfo.InvariantCulture) ?? fallback;
                }
            }
            catch { return fallback; }
        }

        public void Write(string name, string value)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RegPath))
                    k?.SetValue(name, value ?? "");
            }
            catch { }
        }
    }
}
