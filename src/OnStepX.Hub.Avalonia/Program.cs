using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using Avalonia;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Diagnostics;
using ASCOM.OnStepX.Hardware;
using ASCOM.OnStepX.Hardware.Transport;
using ASCOM.OnStepX.Hub.Platform;
using ASCOM.OnStepX.Notifications;
using ASCOM.OnStepX.Platform;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX
{
    // Entry point for the cross-platform hub.
    //
    // Keeps the WPF hub's contract so the two are interchangeable while both
    // exist: the same global single-instance mutex (only one hub of either
    // flavour owns the mount), the same named pipe for ASCOM clients, and the
    // same IPC SHOWHUB handshake that makes a second launch raise the first
    // one's window instead of starting another.
    //
    // .NET implements named pipes over Unix domain sockets, so the pipe server
    // runs on Linux and macOS too. Nothing connects to it there yet — the
    // ASCOM driver is Windows-only by definition — but the LX200 TCP bridge
    // that Ekos will use goes through the same MountSession, so leaving it
    // running costs nothing and keeps one startup path.
    internal static class Program
    {
        internal static Mutex SingleInstanceMutex;
        private static HubPipeServer _pipeServer;
        private static Lx200TcpServer _lx200Bridge;

        [STAThread]
        public static int Main(string[] args)
        {
            try { return Run(args); }
            catch (Exception fatal)
            {
                try
                {
                    File.AppendAllText(
                        Path.Combine(Path.GetTempPath(), "OnStepX.Hub.fatal.log"),
                        DateTime.Now.ToString("u") + "  " + fatal + Environment.NewLine);
                }
                catch { }
                Console.Error.WriteLine("OnStepX.Hub fatal error: " + fatal);
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            SingleInstanceMutex = new Mutex(true, @"Global\OnStepX.Hub.SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                TrySignalExistingHubToShow();
                return 0;
            }

            // Before DriverSettings reads its first value and before any view
            // model is constructed.
            InstallPlatform();

            DriverSettings.RunMigrations();
            DebugLogger.Init("hub");
            MountAlertBridge.Attach(MountSession.Instance);

            _pipeServer = new HubPipeServer(MountSession.Instance, showHubHandler: ShowMainWindow);
            _pipeServer.Start();

            // The LX200 bridge is what Ekos talks to. Off unless asked for,
            // because it is a port that can slew a telescope.
            if (DriverSettings.Lx200BridgeEnabled)
            {
                _lx200Bridge = new Lx200TcpServer(
                    MountSession.Instance,
                    DriverSettings.Lx200BridgePort,
                    DriverSettings.Lx200BridgeAllowRemote);
                _lx200Bridge.Start();
            }

            int rc = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

            try { _lx200Bridge?.Stop(); } catch { }
            try { _pipeServer?.Stop(); } catch { }
            try { MountSession.Instance.ForceCloseAll(); } catch { }
            try { DebugLogger.Shutdown(); } catch { }
            try { SingleInstanceMutex?.ReleaseMutex(); } catch { }
            return rc;
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                      .UsePlatformDetect()
                      .LogToTrace();

        // Where OnStepX.Core reaches this host. The only place that knows the
        // UI toolkit is Avalonia.
        private static void InstallPlatform()
        {
            var dialogs = new AvaloniaDialogs();

            Ui.Install(
                dialogs:    dialogs,
                dispatcher: new AvaloniaDispatcher(),
                shell:      new AvaloniaShell(),
                notifier:   new DesktopNotifier(),
                windows:    new AvaloniaWindowService(dialogs),
                settings:   new JsonSettingsStore(),
                files:      new AvaloniaFilePicker(),
                location:   new UnavailableLocationProvider());

            CommandRequery.Install(new AvaloniaCommandRequerySource(CommandRequery.Source));
        }

        private static void ShowMainWindow()
        {
            Ui.Dispatcher.Post(() =>
            {
                if (Avalonia.Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
                    && d.MainWindow is { } w)
                {
                    w.Show();
                    if (w.WindowState == Avalonia.Controls.WindowState.Minimized)
                        w.WindowState = Avalonia.Controls.WindowState.Normal;
                    w.Activate();
                }
            });
        }

        private static void TrySignalExistingHubToShow()
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeTransport.PIPE_NAME, PipeDirection.InOut);
                pipe.Connect(1500);
                using var w = new StreamWriter(pipe) { AutoFlush = true };
                using var r = new StreamReader(pipe);
                w.WriteLine("IPC:SHOWHUB");
                r.ReadLine();
            }
            catch { /* silent, exactly as the WPF hub does */ }
        }
    }
}
