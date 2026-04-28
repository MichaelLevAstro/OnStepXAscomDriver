using System;
using System.Drawing;
using System.IO.Pipes;
using System.Threading;
using System.Windows.Forms;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Diagnostics;
using ASCOM.OnStepX.Hardware;
using ASCOM.OnStepX.Hardware.Transport;
using ASCOM.OnStepX.Notifications;
using ASCOM.OnStepX.Ui;

namespace ASCOM.OnStepX
{
    internal static class Program
    {
        internal static Mutex SingleInstanceMutex;
        internal static HubForm Hub;
        private static HubPipeServer _pipeServer;
        private static NotifyIcon _tray;

        [STAThread]
        private static int Main(string[] args)
        {
            bool startInTray = false;
            foreach (string a in args)
            {
                string arg = a.TrimStart('-', '/').ToLowerInvariant();
                if (arg == "tray") { startInTray = true; break; }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Global\ scope: Fast User Switching must not produce a second hub
            // in another session — driver auto-launch would end up talking to
            // the wrong pipe owner. Rename also avoids colliding with any
            // residual v0.3.16 mutex if user side-by-sides during the upgrade.
            bool createdNew;
            SingleInstanceMutex = new Mutex(true, @"Global\OnStepX.Hub.SingleInstance", out createdNew);
            if (!createdNew)
            {
                // Another hub is up — tell it to surface then exit quietly.
                TrySignalExistingHubToShow();
                return 0;
            }

            // Run before HubForm reads SiteLongitude / SiteStore. Idempotent.
            DriverSettings.RunMigrations();

            DebugLogger.Init("hub");

            MountAlertBridge.Attach(MountSession.Instance);

            Hub = new HubForm();
            _pipeServer = new HubPipeServer(MountSession.Instance,
                showHubHandler: () => { try { Hub?.EnsureVisibleFromClient(); } catch { } });
            _pipeServer.Start();

            BuildTray();
            MountAlertBridge.Attach(MountSession.Instance);

            if (startInTray)
            {
                Hub.WindowState = FormWindowState.Minimized;
                Hub.ShowInTaskbar = false;
                // Create handle without showing — needed so BeginInvoke works
                // when the first IPC:SHOWHUB arrives.
                var _ = Hub.Handle;
            }
            else
            {
                Hub.Show();
            }

            Application.Run();

            try { _pipeServer?.Stop(); } catch { }
            try { MountSession.Instance.ForceCloseAll(); } catch { }
            try { _tray?.Dispose(); } catch { }
            try { DebugLogger.Shutdown(); } catch { }
            try { SingleInstanceMutex?.ReleaseMutex(); } catch { }
            return 0;
        }

        private static void BuildTray()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Show", null, (s, e) => Hub?.EnsureVisibleFromClient());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Connect", null, (s, e) => { Hub?.EnsureVisibleFromClient(); Hub?.RequestConnect(); });
            menu.Items.Add("Disconnect", null, (s, e) => Hub?.RequestDisconnect());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => { try { Hub?.Close(); } catch { } Application.Exit(); });

            _tray = new NotifyIcon
            {
                Icon = Ui.AppIcons.App,
                Text = "OnStepX Hub",
                ContextMenuStrip = menu,
                Visible = true
            };
            _tray.DoubleClick += (s, e) => Hub?.EnsureVisibleFromClient();
        }

        // Best-effort: if another hub is already running and this invocation
        // was a user double-click (or a driver trying to auto-launch during a
        // hub-already-up race), pop its window so the user sees it.
        private static void TrySignalExistingHubToShow()
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", PipeTransport.PIPE_NAME, PipeDirection.InOut))
                {
                    pipe.Connect(1500);
                    using (var w = new System.IO.StreamWriter(pipe) { AutoFlush = true })
                    using (var r = new System.IO.StreamReader(pipe))
                    {
                        w.WriteLine("IPC:SHOWHUB");
                        r.ReadLine();
                    }
                }
            }
            catch { /* primary not reachable; silent */ }
        }
    }
}
