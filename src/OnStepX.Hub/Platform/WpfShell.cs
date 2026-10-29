using System;
using System.Diagnostics;
using System.Windows;
using ASCOM.OnStepX.Hardware.Transport;
using ASCOM.OnStepX.Platform;
using ASCOM.OnStepX.Services;

namespace ASCOM.OnStepX.Hub.Platform
{
    // IShell over WPF's Application / MainWindow, plus the clipboard and the
    // desktop. Each member is the expression the view models used to inline.
    internal sealed class WpfShell : IShell
    {
        public void Minimize()
        {
            var w = Application.Current?.MainWindow;
            if (w != null) w.WindowState = WindowState.Minimized;
        }

        public void ToggleMaximize()
        {
            var w = Application.Current?.MainWindow;
            if (w == null) return;
            w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        public void CloseMainWindow() => Application.Current?.MainWindow?.Close();

        public void Shutdown() => Application.Current?.Shutdown();

        public void ToggleTheme() => ThemeService.Toggle();

        public void SetClipboardText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text); } catch { }
        }

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
    }
}
