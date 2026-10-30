using System;
using System.Collections.Generic;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Hardware.Transport;
using ASCOM.OnStepX.Platform;

namespace ASCOM.OnStepX.Notifications
{
    // Debounce and preference in front of INotifier. Process-wide static so
    // any hub component (UI thread, MountStateCache poll thread, slew dialog)
    // can fire-and-forget a notification without holding a service instance.
    //
    // The toast itself is the host's job — Windows toasts here, libnotify or
    // osascript elsewhere. NotificationSeverity now comes from the platform
    // namespace so there is one of it rather than two.
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

            string key = (title ?? "") + "" + (body ?? "");
            lock (_gate)
            {
                if (_recent.TryGetValue(key, out var last) && DateTime.UtcNow - last < DebounceWindow)
                    return;
                _recent[key] = DateTime.UtcNow;
                PruneRecent();
            }

            try
            {
                Ui.Notifier.Show(title ?? "", body ?? "", sev);
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
