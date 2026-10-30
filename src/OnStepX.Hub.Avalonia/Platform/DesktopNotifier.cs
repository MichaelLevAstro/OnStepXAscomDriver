using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ASCOM.OnStepX.Hardware.Transport;
using ASCOM.OnStepX.Platform;

namespace ASCOM.OnStepX.Hub.Platform
{
    // INotifier for the platforms the Avalonia hub runs on.
    //
    // Linux  : notify-send, which every desktop with a notification daemon
    //          provides. On a headless Pi there is no daemon and the call
    //          fails quietly, which is the right outcome — the alert still
    //          reaches the console and the log.
    // macOS  : osascript's display notification.
    // Windows: nothing yet. The WPF hub's toasts come from
    //          Microsoft.Toolkit.Uwp.Notifications, which is .NET Framework
    //          only; the modern replacement is a separate piece of work and
    //          the WPF hub is still the shipping Windows build.
    //
    // Debouncing and the user's preference stay in NotificationService, in
    // front of this.
    internal sealed class DesktopNotifier : INotifier
    {
        public void Show(string title, string body, NotificationSeverity severity)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) Linux(title, body, severity);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) Mac(title, body);
            }
            catch (Exception ex)
            {
                try { TransportLogger.Note("Notification failed: " + ex.Message); } catch { }
            }
        }

        private static void Linux(string title, string body, NotificationSeverity severity)
        {
            string urgency = severity == NotificationSeverity.Error ? "critical"
                           : severity == NotificationSeverity.Warning ? "normal"
                           : "low";

            Run("notify-send", new[] { "--app-name=OnStepHub", "--urgency=" + urgency, title ?? "", body ?? "" });
        }

        private static void Mac(string title, string body)
        {
            string script = "display notification " + Quote(body ?? "") +
                            " with title " + Quote(title ?? "OnStepHub");
            Run("osascript", new[] { "-e", script });
        }

        // AppleScript strings take backslash escapes, same as C.
        private static string Quote(string s) =>
            "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        // ArgumentList rather than a joined string, so a message containing
        // quotes or spaces cannot turn into extra arguments.
        private static void Run(string exe, string[] args)
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
        }
    }
}
