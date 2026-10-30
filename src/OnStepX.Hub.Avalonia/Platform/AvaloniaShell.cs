using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using ASCOM.OnStepX.Hardware.Transport;
using ASCOM.OnStepX.Platform;
using ASCOM.OnStepX.Services;

namespace ASCOM.OnStepX.Hub.Platform
{
    internal sealed class AvaloniaShell : IShell
    {
        private static IClassicDesktopStyleApplicationLifetime Desktop =>
            Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

        private static Window Main => Desktop?.MainWindow;

        public void Minimize() => OnUi(() =>
        {
            var w = Main;
            if (w != null) w.WindowState = WindowState.Minimized;
        });

        public void ToggleMaximize() => OnUi(() =>
        {
            var w = Main;
            if (w == null) return;
            w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        });

        public void CloseMainWindow() => OnUi(() => Main?.Close());

        public void Shutdown() => OnUi(() => Desktop?.Shutdown());

        public void ToggleTheme() => OnUi(ThemeService.Toggle);

        public void SetClipboardText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            OnUi(async () =>
            {
                try
                {
                    var clip = Main?.Clipboard;
                    if (clip != null) await clip.SetTextAsync(text);
                }
                catch (Exception ex)
                {
                    try { TransportLogger.Note("Clipboard write failed: " + ex.Message); } catch { }
                }
            });
        }

        // Process.Start with UseShellExecute hands off to the desktop on every
        // platform .NET supports: ShellExecute on Windows, xdg-open on Linux,
        // open(1) on macOS. Worth knowing on a Pi: with no desktop session
        // there is nothing to hand it to, and this logs rather than throwing.
        public void OpenExternal(string pathOrUrl)
        {
            if (string.IsNullOrEmpty(pathOrUrl)) return;
            try
            {
                Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                try { TransportLogger.Note("Could not open '" + pathOrUrl + "': " + ex.Message); } catch { }
            }
        }

        private static void OnUi(Action a)
        {
            if (Dispatcher.UIThread.CheckAccess()) a();
            else Dispatcher.UIThread.Post(a);
        }
    }
}
