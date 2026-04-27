using System;
using System.Collections.Generic;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Hardware.Transport;
using Microsoft.Toolkit.Uwp.Notifications;

namespace ASCOM.OnStepX.Notifications
{
    internal enum NotificationSeverity { Info, Warning, Error }

    // Generic Windows toast emitter. Process-wide static so any hub component
    // (UI thread, MountStateCache poll thread, slew dialog) can fire-and-forget
    // a notification without taking a dependency on a service instance.
    //
    // ToastNotificationManagerCompat handles AppUserModelID + start-menu shortcut
    // creation on first call, so no installer plumbing is required for this to
    // work on Windows 10+. On older Windows versions the call silently no-ops
    // (caught in the try/catch and logged via TransportLogger).
    internal static class NotificationService
    {
        private static readonly object _gate = new object();
        private static readonly Dictionary<string, DateTime> _recent = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(30);

        public static bool IsEnabled => DriverSettings.NotificationsEnabled;

        public static void Show(string title, string body, NotificationSeverity sev = NotificationSeverity.Info)
        {
            if (!IsEnabled) return;
            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(body)) return;

            string key = (title ?? "") + "" + (body ?? "");
            lock (_gate)
            {
                if (_recent.TryGetValue(key, out var last) && DateTime.UtcNow - last < DebounceWindow)
                    return;
                _recent[key] = DateTime.UtcNow;
                PruneRecent();
            }

            try
            {
                var builder = new ToastContentBuilder()
                    .AddText(title ?? "")
                    .AddText(body ?? "")
                    .AddAttributionText("OnStepX Hub");

                if (sev == NotificationSeverity.Warning || sev == NotificationSeverity.Error)
                    builder.SetToastScenario(ToastScenario.Reminder);

                builder.Show();
            }
            catch (Exception ex)
            {
                try { TransportLogger.Note("Notification failed: " + ex.Message); } catch { }
            }
        }

        private static void PruneRecent()
        {
            if (_recent.Count < 64) return;
            var cutoff = DateTime.UtcNow - DebounceWindow;
            var stale = new List<string>();
            foreach (var kv in _recent)
                if (kv.Value < cutoff) stale.Add(kv.Key);
            foreach (var k in stale) _recent.Remove(k);
        }
    }
}
