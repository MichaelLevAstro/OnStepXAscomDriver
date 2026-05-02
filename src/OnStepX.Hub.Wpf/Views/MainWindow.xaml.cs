using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using ASCOM.OnStepX.Hardware.State;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Views
{
    public partial class MainWindow : Window
    {
        public MainViewModel VM { get; }
        // Set by RequestExit() so the Closing handler stops intercepting and
        // lets the window actually close. Without this flag the X button can
        // never close the app — it only hides to tray, leaving Application.Run
        // looping until tray Exit is invoked.
        private bool _exiting;

        public MainWindow()
        {
            InitializeComponent();
            VM = new MainViewModel();
            DataContext = VM;
            try { Icon = WindowIconLoader.LoadImageSource(); } catch { }
            Loaded += (s, e) => VM.TryAutoConnect();
            Closed += MainWindow_Closed;
            Closing += MainWindow_Closing;
        }

        // X button policy:
        //   * No ASCOM clients connected → disconnect the mount and exit the
        //     app entirely. The tray icon is only meaningful while clients are
        //     attached; otherwise it's just an orphan in the notification area.
        //   * Clients connected → prompt with three branches:
        //       Yes    : hide to tray (keep mediating IPC)
        //       No     : close anyway — clients will see a transport drop
        //       Cancel : keep window open
        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_exiting) return;

            if (ClientRegistry.Count > 0)
            {
                var r = MessageBox.Show(this,
                    ClientRegistry.Count + " ASCOM client(s) still connected.\r\n\r\n" +
                    "Yes  = hide to tray (keep serving clients)\r\n" +
                    "No   = close anyway (clients will disconnect)\r\n" +
                    "Cancel = keep window open",
                    "OnStepX", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
                if (r == MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    Hide();
                    return;
                }
                // No: fall through to full-close path.
            }

            _exiting = true;
            try { VM?.Connection?.DoDisconnect(); } catch { }
            // e.Cancel stays false — Window proceeds to close, Closed fires,
            // Application.Current.Shutdown() drops the dispatcher and
            // Program.MainImpl returns from app.Run().
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            try { VM?.Detach(); } catch { }
            try { Application.Current?.Shutdown(); } catch { }
        }

        // Tray "Show" / IPC SHOWHUB. Surfaces the window if minimized/hidden,
        // and forces foreground so a click on the tray icon doesn't end up with
        // a flashing taskbar entry that the user has to click again.
        public void EnsureVisibleFromClient()
        {
            try
            {
                if (!IsVisible) Show();
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
                Topmost = true; Topmost = false;
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero) SetForegroundWindow(hwnd);
            }
            catch { }
        }

        public void RequestConnect()    => VM?.Connection?.DoConnect();
        public void RequestDisconnect() => VM?.Connection?.DoDisconnect();

        // Tray Exit path. Disconnects the mount before closing so the tray
        // exit always tears down the wire, mirroring the no-clients X-close
        // path. Closed handler does the Application.Shutdown.
        public void RequestExit()
        {
            _exiting = true;
            try { VM?.Connection?.DoDisconnect(); } catch { }
            try { Close(); } catch { }
        }

        // COM port dropdown opened — refresh the list. SerialPort.GetPortNames
        // is cheap; doing it on demand keeps the list fresh when adapters are
        // hot-plugged after the hub started.
        private void ComPortDropDownOpened(object sender, EventArgs e)
        {
            VM?.Connection?.RefreshSerialPorts();
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
    }

    internal static class WindowIconLoader
    {
        public static System.Windows.Media.ImageSource LoadImageSource()
        {
            using (var stream = typeof(WindowIconLoader).Assembly
                       .GetManifestResourceStream("ASCOM.OnStepX.AppIcon.ico"))
            {
                if (stream == null) return null;
                var dec = new System.Windows.Media.Imaging.IconBitmapDecoder(
                    stream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                return dec.Frames[0];
            }
        }
    }
}
