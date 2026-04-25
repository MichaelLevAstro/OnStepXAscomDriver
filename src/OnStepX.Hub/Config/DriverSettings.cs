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
        // Sync distance guardrail (degrees). 0 disables the check; any positive
        // value triggers a confirmation popup in the driver process when an
        // ASCOM sync would move the mount's reported position by more than
        // this angular distance — a large delta usually means the site or
        // time is wrong rather than a legitimate plate-solve correction.
        // Read by the driver directly from registry (same HKCU key) so the
        // hub and driver processes share one source of truth.
        public static int    SyncLimitDeg { get => GetInt("SyncLimitDeg", 0); set => SetInt("SyncLimitDeg", value); }

        public static double SlewRateDegPerSec { get => GetDouble("SlewRateDegPerSec", 3.0); set => SetDouble("SlewRateDegPerSec", value); }
        public static double GuideRateMultiplier { get => GetDouble("GuideRateMultiplier", 0.5); set => SetDouble("GuideRateMultiplier", value); }
        public static bool   MeridianAutoFlip { get => GetBool("MeridianAutoFlip", true); set => SetBool("MeridianAutoFlip", value); }

        // Advanced pier/flip policy. Values match OnStepX :SX96 chars: B/E/W/A.
        // Default "B" (Best) stays on current side when possible — safe for
        // plate-solve-near-meridian workflows. Pause-at-home on flip is
        // write-only at firmware level (no :GX98), so driver settings are
        // authoritative and re-applied on every connect.
        public static string PreferredPierSide { get => Get("PreferredPierSide", "B"); set => Set("PreferredPierSide", value); }
        public static bool   PauseAtHomeOnFlip { get => GetBool("PauseAtHomeOnFlip", false); set => SetBool("PauseAtHomeOnFlip", value); }

        public static bool   AutoConnect { get => GetBool("AutoConnect", true); set => SetBool("AutoConnect", value); }
        public static bool   AutoSyncTimeOnConnect { get => GetBool("AutoSyncTimeOnConnect", true); set => SetBool("AutoSyncTimeOnConnect", value); }

        // When slewing to a Sun/Moon/planet from the Slew dialog, switch the
        // mount tracking rate (:TS#/:TL#/:TQ#) to match. Without this the
        // mount keeps sidereal and Moon/Sun visibly drift off frame within
        // minutes. Default ON; off for users who manage tracking rate manually.
        public static bool   AutoSwitchPlanetTrackingRate { get => GetBool("AutoSwitchPlanetTrackingRate", true); set => SetBool("AutoSwitchPlanetTrackingRate", value); }

        // "dark" or "light". Default dark per redesign.
        public static string Theme { get => Get("Theme", "dark"); set => Set("Theme", value); }

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
