using System;
using ASCOM.OnStepX.Hardware.Transport;
using ASCOM.OnStepX.Platform;
using Microsoft.Toolkit.Uwp.Notifications;

namespace ASCOM.OnStepX.Hub.Platform
{
    // Windows toast, moved out of NotificationService unchanged.
    //
    // ToastNotificationManagerCompat handles AppUserModelID and the start-menu
    // shortcut on first call, so no installer plumbing is needed for this to
    // work on Windows 10+. On older Windows the call no-ops into the catch.
    //
    // Debouncing and the user's enable/disable preference stay in
    // NotificationService, in front of this.
    internal sealed class WindowsNotifier : INotifier
    {
        public void Show(string title, string body, NotificationSeverity severity)
        {
            try
            {
                var builder = new ToastContentBuilder()
                    .AddText(title ?? "")
                    .AddText(body ?? "")
                    .AddAttributionText("OnStepHub");

                if (severity == NotificationSeverity.Warning || severity == NotificationSeverity.Error)
                    builder.SetToastScenario(ToastScenario.Reminder);

                builder.Show();
            }
            catch (Exception ex)
            {
                try { TransportLogger.Note("Notification failed: " + ex.Message); } catch { }
            }
        }
    }
}
