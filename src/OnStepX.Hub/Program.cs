using System;
using System.IO.Pipes;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Diagnostics;
using ASCOM.OnStepX.Hardware;
using ASCOM.OnStepX.Hardware.Transport;
using ASCOM.OnStepX.Notifications;
using ASCOM.OnStepX.Views;
using Application = System.Windows.Application;

namespace ASCOM.OnStepX
{
    // Mirrors the legacy WinForms Program.cs (Hub/Program.cs) so the WPF hub
    // is interchangeable with the original: same single-instance mutex (so
    // only one hub of either flavor runs), same named-pipe server, same tray,
    // same /tray launch flag, same IPC SHOWHUB protocol.
    //
    // Program.Hub remains a System.Windows.Forms.Form for compatibility with
    // SyncLimitGuard's PromptOnUiThread (and any future code that takes
    // an IWin32Window owner). It's a hidden, never-shown form whose handle
    // sits on the UI/STA thread and provides Invoke semantics for the cross-
    // thread MessageBox prompt.
    internal static class Program
    {
        internal static Mutex SingleInstanceMutex;
        internal static System.Windows.Forms.Form Hub;
        internal static MainWindow MainWindow;
        private static HubPipeServer _pipeServer;
        private static NotifyIcon _tray;

        [STAThread]
        private static int Main(string[] args)
        {
            try { return MainImpl(args); }
            catch (Exception fatal)
            {
                try
                {
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OnStepX.Hub.fatal.log"),
                        DateTime.Now.ToString("u") + "  " + fatal + "\r\n");
                }
                catch { }
                System.Windows.Forms.MessageBox.Show("OnStepX.Hub fatal error:\r\n\r\n" + fatal,
                    "OnStepX", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                return 1;
            }
        }

        private static int MainImpl(string[] args)
        {
            bool startInTray = false;
            foreach (string a in args)
            {
                string arg = a.TrimStart('-', '/').ToLowerInvariant();
                if (arg == "tray") { startInTray = true; break; }
            }

            // WinForms message loop is required because we host a NotifyIcon
            // (tray) and a hidden compat Form alongside the WPF dispatcher.
            // EnableVisualStyles is harmless even though no WinForms UI is
            // shown to the user.
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

            bool createdNew;
            SingleInstanceMutex = new Mutex(true, @"Global\OnStepX.Hub.SingleInstance", out createdNew);
            if (!createdNew)
            {
                TrySignalExistingHubToShow();
                return 0;
            }

            DriverSettings.RunMigrations();
            DebugLogger.Init("hub");
            MountAlertBridge.Attach(MountSession.Instance);

            // Hidden compat Form for SyncLimitGuard.PromptOnUiThread Invoke.
            // Created on the STA thread; Handle realized so InvokeRequired
            // returns a meaningful answer from worker threads.
            Hub = new System.Windows.Forms.Form
            {
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.None,
                Opacity = 0,
                Size = new System.Drawing.Size(1, 1),
                StartPosition = FormStartPosition.Manual,
                Location = new System.Drawing.Point(-32000, -32000)
            };
            var _h = Hub.Handle;

            var app = new App();

            MainWindow = new MainWindow();
            app.MainWindow = MainWindow;

            _pipeServer = new HubPipeServer(MountSession.Instance,
                showHubHandler: () =>
                {
                    try { MainWindow?.Dispatcher.BeginInvoke((Action)(() => MainWindow.EnsureVisibleFromClient())); }
                    catch { }
                });
            _pipeServer.Start();

            BuildTray();

            if (!startInTray)
            {
                MainWindow.Show();
            }
            else
            {
                // Realize handle without showing so dispatcher invokes work
                // when the first IPC:SHOWHUB arrives.
                var _ = new System.Windows.Interop.WindowInteropHelper(MainWindow).EnsureHandle();
            }

            int rc = app.Run();

            try { _pipeServer?.Stop(); } catch { }
            try { MountSession.Instance.ForceCloseAll(); } catch { }
            try { _tray?.Dispose(); } catch { }
            try { DebugLogger.Shutdown(); } catch { }
            try { SingleInstanceMutex?.ReleaseMutex(); } catch { }
            return rc;
        }

        private static void BuildTray()
        {
            // Tray menu handlers run on the WinForms message-pump thread (the
            // same STA thread as the WPF dispatcher in this hosting setup), but
            // we still BeginInvoke onto the Dispatcher to keep all UI mutations
            // on the WPF queue and avoid any reentrancy with an open Popup or
            // a pending render pass.
            var menu = new ContextMenuStrip();
            menu.Items.Add("Show", null, (s, e) => DispatchToMain(w => w.EnsureVisibleFromClient()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Connect",    null, (s, e) => DispatchToMain(w => { w.EnsureVisibleFromClient(); w.RequestConnect(); }));
            menu.Items.Add("Disconnect", null, (s, e) => DispatchToMain(w => w.RequestDisconnect()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) =>
            {
                if (MainWindow != null)
                {
                    try { MainWindow.Dispatcher.BeginInvoke(new Action(() => MainWindow.RequestExit())); }
                    catch { try { Application.Current?.Shutdown(); } catch { } }
                }
                else
                {
                    try { Application.Current?.Shutdown(); } catch { }
                }
            });

            System.Drawing.Icon icon;
            try { icon = AppIconLoader.Load(); }
            catch { icon = System.Drawing.SystemIcons.Application; }

            _tray = new NotifyIcon
            {
                Icon = icon,
                Text = "OnStepX Hub",
                ContextMenuStrip = menu,
                Visible = true
            };
            _tray.DoubleClick += (s, e) => DispatchToMain(w => w.EnsureVisibleFromClient());
        }

        private static void DispatchToMain(Action<Views.MainWindow> action)
        {
            var w = MainWindow;
            if (w == null) return;
            try { w.Dispatcher.BeginInvoke(new Action(() => action(w))); }
            catch { }
        }

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
            catch { /* silent */ }
        }
    }

    // Ports the WinForms AppIcons.cs reflective-load behavior so the tray
    // gets the embedded AppIcon.ico without a separate resource file.
    internal static class AppIconLoader
    {
        public static System.Drawing.Icon Load()
        {
            using (var stream = typeof(AppIconLoader).Assembly
                       .GetManifestResourceStream("ASCOM.OnStepX.AppIcon.ico"))
            {
                if (stream == null) throw new System.IO.FileNotFoundException("AppIcon.ico embedded resource missing");
                return new System.Drawing.Icon(stream);
            }
        }
    }
}
