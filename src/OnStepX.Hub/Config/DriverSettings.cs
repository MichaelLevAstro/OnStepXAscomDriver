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
        // Driver-side sync distance guardrail (degrees). 0 disables.
        public static int    SyncLimitDeg { get => GetInt("SyncLimitDeg", 0); set => SetInt("SyncLimitDeg", value); }

        public static double SlewRateDegPerSec { get => GetDouble("SlewRateDegPerSec", 3.0); set => SetDouble("SlewRateDegPerSec", value); }
        public static double GuideRateMultiplier { get => GetDouble("GuideRateMultiplier", 0.5); set => SetDouble("GuideRateMultiplier", value); }
        public static bool   MeridianAutoFlip { get => GetBool("MeridianAutoFlip", true); set => SetBool("MeridianAutoFlip", value); }

        // OnStepX :SX96 chars: B/E/W/A.
        public static string PreferredPierSide { get => Get("PreferredPierSide", "B"); set => Set("PreferredPierSide", value); }
        public static bool   PauseAtHomeOnFlip { get => GetBool("PauseAtHomeOnFlip", false); set => SetBool("PauseAtHomeOnFlip", value); }

        public static bool   AutoConnect { get => GetBool("AutoConnect", true); set => SetBool("AutoConnect", value); }
        public static bool   AutoSyncTimeOnConnect { get => GetBool("AutoSyncTimeOnConnect", true); set => SetBool("AutoSyncTimeOnConnect", value); }

        // Hub toast notifications (limit reached, etc).
        public static bool   NotificationsEnabled { get => GetBool("NotificationsEnabled", true); set => SetBool("NotificationsEnabled", value); }

        // Console pane shown/hidden state, persisted across sessions.
        public static bool   ConsoleVisible { get => GetBool("ConsoleVisible", false); set => SetBool("ConsoleVisible", value); }

        // Last selected tab id ("setup"/"main"/"extra"/"polar"/"adv"). Restored on launch.
        public static string ActiveTab { get => Get("ActiveTab", "main"); set => Set("ActiveTab", value ?? "main"); }

        // Per-section expanded/collapsed state, keyed by Section.PersistKey.
        public static bool   GetSectionExpanded(string key, bool defaultValue) => GetBool("Section." + key + ".Expanded", defaultValue);
        public static void   SetSectionExpanded(string key, bool value) => SetBool("Section." + key + ".Expanded", value);

        // Persistent log file under %APPDATA%\OnStepX\logs. ON = every line
        // shown in the hub console is also written to disk.
        public static bool   VerboseFileLog { get => GetBool("VerboseFileLog", false); set => SetBool("VerboseFileLog", value); }

        // Auto-switch tracking rate to match Sun/Moon/planet target.
        public static bool   AutoSwitchPlanetTrackingRate { get => GetBool("AutoSwitchPlanetTrackingRate", true); set => SetBool("AutoSwitchPlanetTrackingRate", value); }

        // Focuser preferences. Backlash / TCF coefficients live in firmware NV;
        // hub reads on connect, writes through on edit, no caching here.
        public static int    FocuserDefaultIndex { get => GetInt("FocuserDefaultIndex", 1); set => SetInt("FocuserDefaultIndex", value); }
        public static bool   FocuserAutoExpand   { get => GetBool("FocuserAutoExpand", true); set => SetBool("FocuserAutoExpand", value); }
        // Per-click step size for the FOCUSER section In/Out buttons.
        public static int    FocuserStepSize     { get => GetInt("FocuserStepSize", 100); set => SetInt("FocuserStepSize", value); }

        // Rotator preferences. Reverse + sync offset are driver-side state
        // (ASCOM IRotatorV3.Reverse / Sync). Persisted on the same registry
        // root so the in-proc driver and the hub share one config surface.
        public static bool   RotatorReverse      { get => GetBool("RotatorReverse", false); set => SetBool("RotatorReverse", value); }
        public static double RotatorSyncOffsetDeg{ get => GetDouble("RotatorSyncOffsetDeg", 0.0); set => SetDouble("RotatorSyncOffsetDeg", value); }
        // Rate combo selections survive disconnect/reconnect.
        public static int    RotatorMoveRatePreset { get => GetInt("RotatorMoveRatePreset", 3); set => SetInt("RotatorMoveRatePreset", value); }
        public static int    RotatorGotoRatePreset { get => GetInt("RotatorGotoRatePreset", 7); set => SetInt("RotatorGotoRatePreset", value); }
        // Last displayed rotator angle (degrees, 0..360). Saved on each poll;
        // OnStepX firmware only persists position to NV when explicitly parked,
        // so cold boots typically come back at 0°. The Hub uses this to offer
        // a one-click "Restore last angle" sync after such a power cycle.
        public static double RotatorLastAngleDeg { get => GetDouble("RotatorLastAngleDeg", double.NaN); set => SetDouble("RotatorLastAngleDeg", value); }

        public static string Theme { get => Get("Theme", "dark"); set => Set("Theme", value); }

        // PA wedge: AXIS4 = Alt, AXIS5 = Az.
        public static bool   PolarAlignmentMode { get => GetBool("PolarAlignmentMode", false); set => SetBool("PolarAlignmentMode", value); }
        public static int    PolarAlignAltStepSize { get => GetInt("PolarAlignAltStepSize", 100); set => SetInt("PolarAlignAltStepSize", value); }
        public static int    PolarAlignAzStepSize  { get => GetInt("PolarAlignAzStepSize",  100); set => SetInt("PolarAlignAzStepSize",  value); }
        public static int    PolarAlignAltRunCurrent  { get => GetInt("PolarAlignAltRunCurrent",  500); set => SetInt("PolarAlignAltRunCurrent",  value); }
        public static int    PolarAlignAzRunCurrent   { get => GetInt("PolarAlignAzRunCurrent",   500); set => SetInt("PolarAlignAzRunCurrent",   value); }
        public static int    PolarAlignAltHoldPercent { get => GetInt("PolarAlignAltHoldPercent", 50);  set => SetInt("PolarAlignAltHoldPercent", value); }
        public static int    PolarAlignAzHoldPercent  { get => GetInt("PolarAlignAzHoldPercent",  50);  set => SetInt("PolarAlignAzHoldPercent",  value); }
        public static string TppaBridgePort { get => Get("TppaBridgePort", ""); set => Set("TppaBridgePort", value); }
        public static int    LastSeenManagedPairCount { get => GetInt("LastSeenManagedPairCount", 0); set => SetInt("LastSeenManagedPairCount", value); }

        // Auto-routes TppaBridgePort to a Hub-managed com0com pair's A side.
        // Resets stale references (port matches a PortB, or no longer in the
        // managed list) so a fresh installer-created pair always wins.
        public static bool EnsureTppaBridgePortDefaulted()
        {
            try
            {
                var pairs = Hardware.Tppa.Com0comManager.GetManagedPairsFromRegistry();
                if (pairs == null || pairs.Count == 0)
                {
                    LastSeenManagedPairCount = 0;
                    return false;
                }
                LastSeenManagedPairCount = pairs.Count;

                string current = (TppaBridgePort ?? "").Trim();
                string firstPortA = null;
                bool currentMatchesPortA = false;
                bool currentMatchesPortB = false;
                foreach (var p in pairs)
                {
                    if (firstPortA == null && !string.IsNullOrEmpty(p.PortA)) firstPortA = p.PortA;
                    if (!string.IsNullOrEmpty(current))
                    {
                        if (string.Equals(current, p.PortA, StringComparison.OrdinalIgnoreCase)) currentMatchesPortA = true;
                        if (string.Equals(current, p.PortB, StringComparison.OrdinalIgnoreCase)) currentMatchesPortB = true;
                    }
                }
                if (string.IsNullOrEmpty(firstPortA)) return false;
                if (currentMatchesPortA) return false;
                // Reset on: empty, matches B (user mistake), or stale-orphan.
                if (string.IsNullOrEmpty(current) || currentMatchesPortB || !currentMatchesPortA)
                {
                    TppaBridgePort = firstPortA;
                    return true;
                }
                return false;
            }
            catch { return false; }
        }

        // Longitude on-disk convention. Pre-1: west-positive (raw wire).
        // >=1: east-positive (ASCOM/civil); migration flips once.
        public static int LongitudeConventionVersion
        {
            get => GetInt("LongitudeConventionVersion", 0);
            set => SetInt("LongitudeConventionVersion", value);
        }

        // Idempotent migration runner. Bump version before flipping values
        // so partial failure doesn't double-apply.
        public static void RunMigrations()
        {
            if (LongitudeConventionVersion < 1)
            {
                LongitudeConventionVersion = 1;
                SiteLongitude = -SiteLongitude;

                try
                {
                    var sites = SiteStore.Load();
                    foreach (var s in sites) s.Longitude = -s.Longitude;
                    SiteStore.Save(sites);
                }
                catch { /* sites file unreadable; registry already flipped */ }
            }
        }

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
