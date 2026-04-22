using System;
using System.Globalization;
using Microsoft.Win32;

namespace ASCOM.OnStepX.Config
{
    // Thin registry-backed settings store. Kept independent of ASCOM Profile so unit tests can run.
    internal static class DriverSettings
    {
        private const string RegPath = @"Software\\ASCOM\\OnStepX";

        public static string TransportKind   { get => Get("TransportKind", "Serial"); set => Set("TransportKind", value); }
        public static string SerialPort      { get => Get("SerialPort", "COM3");      set => Set("SerialPort", value); }
        public static int    SerialBaud      { get => GetInt("SerialBaud", 9600);     set => SetInt("SerialBaud", value); }
        public static string TcpHost         { get => Get("TcpHost", "192.168.0.1");  set => Set("TcpHost", value); }
        public static int    TcpPort         { get => GetInt("TcpPort", 9999);        set => SetInt("TcpPort", value); }

        public static double SiteLatitude    { get => GetDouble("SiteLatitude", 0);   set => SetDouble("SiteLatitude", value); }
        public static double SiteLongitude   { get => GetDouble("SiteLongitude", 0);  set => SetDouble("SiteLongitude", value); }
        public static double SiteElevation   { get => GetDouble("SiteElevation", 0);  set => SetDouble("SiteElevation", value); }

        public static int    HorizonLimitDeg { get => GetInt("HorizonLimitDeg", 0);   set => SetInt("HorizonLimitDeg", value); }
        public static int    OverheadLimitDeg{ get => GetInt("OverheadLimitDeg", 85); set => SetInt("OverheadLimitDeg", value); }
        public static int    MeridianLimitEastMin { get => GetInt("MeridianLimitEastMin", 15); set => SetInt("MeridianLimitEastMin", value); }
        public static int    MeridianLimitWestMin { get => GetInt("MeridianLimitWestMin", 15); set => SetInt("MeridianLimitWestMin", value); }

        public static double SlewRateDegPerSec { get => GetDouble("SlewRateDegPerSec", 3.0); set => SetDouble("SlewRateDegPerSec", value); }
        public static double GuideRateMultiplier { get => GetDouble("GuideRateMultiplier", 0.5); set => SetDouble("GuideRateMultiplier", value); }
        public static bool   MeridianAutoFlip { get => GetBool("MeridianAutoFlip", true); set => SetBool("MeridianAutoFlip", value); }

        public static bool   AutoConnect { get => GetBool("AutoConnect", true); set => SetBool("AutoConnect", value); }
        public static bool   AutoSyncTimeOnConnect { get => GetBool("AutoSyncTimeOnConnect", true); set => SetBool("AutoSyncTimeOnConnect", value); }

        private static string Get(string name, string def)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RegPath))
            {
                var v = k.GetValue(name);
                if (v == null) return def;
                return Convert.ToString(v, CultureInfo.InvariantCulture) ?? def;
            }
        }
        private static void Set(string name, string v)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RegPath)) k.SetValue(name, v ?? "");
        }
        private static int GetInt(string name, int def) { return int.TryParse(Get(name, def.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def; }
        private static void SetInt(string name, int v) { Set(name, v.ToString(CultureInfo.InvariantCulture)); }
        private static double GetDouble(string name, double def) { return double.TryParse(Get(name, def.ToString(CultureInfo.InvariantCulture)), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def; }
        private static void SetDouble(string name, double v) { Set(name, v.ToString("G", CultureInfo.InvariantCulture)); }
        private static bool GetBool(string name, bool def) { return bool.TryParse(Get(name, def.ToString()), out var v) ? v : def; }
        private static void SetBool(string name, bool v) { Set(name, v.ToString()); }
    }
}
