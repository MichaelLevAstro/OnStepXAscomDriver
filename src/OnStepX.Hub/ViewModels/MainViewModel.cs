using System;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Diagnostics;
using ASCOM.OnStepX.Hardware;
using ASCOM.OnStepX.Hardware.State;
using ASCOM.OnStepX.Hardware.Tppa;
using ASCOM.OnStepX.Hardware.Transport;
using ASCOM.OnStepX.Services;

namespace ASCOM.OnStepX.ViewModels
{
    // Composition root. Owns the section VMs, the connection state machine,
    // and the 250 ms poll pump that drives MountStateCache → VM properties.
    // Mirrors the bookkeeping that HubForm performs in its constructor and
    // RefreshStatus tick.
    public sealed class MainViewModel : ViewModelBase
    {
        public ConnectionViewModel Connection { get; }
        public SiteViewModel Site { get; }
        public DateTimeViewModel DateTime { get; }
        public TrackingViewModel Tracking { get; }
        public LimitsViewModel Limits { get; }
        public PositionViewModel Position { get; }
        public ParkHomeViewModel ParkHome { get; }
        public ConsoleViewModel Console { get; }
        public AdvancedDiagnosticsViewModel Advanced { get; }
        public VisualizerViewModel Visualizer { get; }
        public FocuserViewModel Focuser { get; }
        public RotatorViewModel Rotator { get; }
        public PolarAlignmentViewModel PolarAlignment { get; }
        public PierMeridianViewModel PierMeridian { get; }
        public MountSettingsViewModel MountSettings { get; }
        public UpdateViewModel Update { get; }

        // Drives the third-column collapse when the firmware exposes neither
        // focuser nor rotator nor PA mode. MainWindow listens to PropertyChanged
        // on this to resize the window between 3-column and 2-column widths.
        public bool Show3rdColumn => Focuser.IsAvailable || Rotator.IsAvailable || PolarAlignment.IsAvailable;

        // Hint card visibility — "no focuser / no rotator detected" hints.
        // PA mode forces Focuser.IsAvailable / Rotator.IsAvailable false on
        // purpose; without the PA gate, both hint cards would appear next to
        // the polar alignment panel and confuse the user.
        public bool ShowFocuserHint => !Focuser.IsAvailable && !PolarAlignment.IsAvailable;
        public bool ShowRotatorHint => !Rotator.IsAvailable && !PolarAlignment.IsAvailable;

        // Extra tab has nothing useful to show: no focuser, no rotator, and PA
        // mode either off (so neither hint card has a special reason to show)
        // or on (which suppresses both Focuser and Rotator regardless). In
        // either case the ExtraTab renders a single centered placeholder.
        public bool IsExtraTabEmpty => !Focuser.IsAvailable && !Rotator.IsAvailable;

        // Active tab id (one of: setup, main, extra, polar, adv). Persisted via
        // DriverSettings.ActiveTab; restored on launch. PA tab visibility is
        // governed by PolarAlignment.IsAvailable; if the active tab disappears,
        // we fall back to "main".
        private string _activeTab = DriverSettings.ActiveTab;
        public string ActiveTab
        {
            get => _activeTab;
            set
            {
                if (string.IsNullOrEmpty(value)) value = "main";
                if (!Set(ref _activeTab, value)) return;
                try { DriverSettings.ActiveTab = value; } catch { }
            }
        }
        public ICommand SetActiveTabCommand { get; }

        // PA tab presence in the tab bar. Mirrors PolarAlignment.IsAvailable so
        // the bar can re-raise on the right thread.
        public bool IsPolarTabVisible => PolarAlignment.IsAvailable;

        private readonly MountSession _mount = MountSession.Instance;
        private readonly DispatcherTimer _pollTimer;
        private readonly TppaSerialBridge _tppaBridge;

        public string AppTitle { get; }
        public string AppVersion { get; }

        public ICommand ToggleThemeCommand { get; }
        public ICommand MinimizeCommand { get; }
        public ICommand MaxRestoreCommand { get; }
        public ICommand CloseCommand { get; }

        private ConnState _state = ConnState.Disconnected;
        public ConnState State
        {
            get => _state;
            private set
            {
                if (!Set(ref _state, value)) return;
                Connection.OnConnStateChanged();
                Site.OnConnStateChanged();
                DateTime.OnConnStateChanged();
                Tracking.OnConnStateChanged();
                Limits.OnConnStateChanged();
                ParkHome.OnConnStateChanged();
                Console.OnConnStateChanged();
                Focuser.OnConnStateChanged();
                Rotator.OnConnStateChanged();
                PolarAlignment.OnConnStateChanged();
                MountSettings.OnConnStateChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        // For SyncConnectionStateFromMount — public IsOpen flag tracks whether
        // the hub considers the mount connected (post Stage-2 responsiveness).
        private bool _hubConnected;

        // Status bar / footer.
        private string _clientCountText = "Connected clients: 0";
        public string ClientCountText { get => _clientCountText; private set => Set(ref _clientCountText, value); }
        private bool _clientCountPulse;
        public bool ClientCountPulse { get => _clientCountPulse; private set => Set(ref _clientCountPulse, value); }
        private StatusKind _clientCountKind = StatusKind.Info;
        public StatusKind ClientCountKind { get => _clientCountKind; private set => Set(ref _clientCountKind, value); }

        public MainViewModel()
        {
            // Run before child VMs read DriverSettings.TppaBridgePort.
            try { DriverSettings.EnsureTppaBridgePortDefaulted(); } catch { }

            Connection = new ConnectionViewModel(this);
            Site       = new SiteViewModel(this);
            DateTime   = new DateTimeViewModel(this);
            Tracking   = new TrackingViewModel(this);
            Limits     = new LimitsViewModel(this);
            Position   = new PositionViewModel();
            ParkHome   = new ParkHomeViewModel(this);
            Console    = new ConsoleViewModel(this);
            Advanced   = new AdvancedDiagnosticsViewModel();
            Visualizer = new VisualizerViewModel();
            Focuser    = new FocuserViewModel(this);
            Rotator    = new RotatorViewModel(this);
            PolarAlignment = new PolarAlignmentViewModel(this);
            PierMeridian   = new PierMeridianViewModel(this);
            MountSettings  = new MountSettingsViewModel(this);
            Update         = new UpdateViewModel();

            // Re-emit Show3rdColumn + hint visibility whenever any child availability flips.
            Focuser.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(FocuserViewModel.IsAvailable))
                {
                    OnPropertyChanged(nameof(Show3rdColumn));
                    OnPropertyChanged(nameof(ShowFocuserHint));
                    OnPropertyChanged(nameof(IsExtraTabEmpty));
                }
            };
            Rotator.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(RotatorViewModel.IsAvailable))
                {
                    OnPropertyChanged(nameof(Show3rdColumn));
                    OnPropertyChanged(nameof(ShowRotatorHint));
                    OnPropertyChanged(nameof(IsExtraTabEmpty));
                }
            };
            PolarAlignment.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PolarAlignmentViewModel.IsAvailable))
                {
                    OnPropertyChanged(nameof(Show3rdColumn));
                    OnPropertyChanged(nameof(ShowFocuserHint));
                    OnPropertyChanged(nameof(ShowRotatorHint));
                    OnPropertyChanged(nameof(IsPolarTabVisible));
                    // PA tab disappeared while the user was on it → fall back to Main.
                    if (!PolarAlignment.IsAvailable && _activeTab == "polar") ActiveTab = "main";
                }
            };

            AppTitle = "OnStepX ASCOM Driver";
            AppVersion = GetVersionString();

            ToggleThemeCommand = new RelayCommand(() => ThemeService.Toggle());
            MinimizeCommand    = new RelayCommand(() => Application.Current.MainWindow.WindowState = WindowState.Minimized);
            MaxRestoreCommand  = new RelayCommand(() =>
            {
                var w = Application.Current.MainWindow;
                w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            });
            CloseCommand       = new RelayCommand(() => Application.Current.MainWindow?.Close());
            SetActiveTabCommand = new RelayCommand(p => { if (p is string id && !string.IsNullOrEmpty(id)) ActiveTab = id; });

            _mount.ConnectionChanged += OnMountConnectionChanged;
            _mount.LimitWarning += OnMountLimitWarning;
            ClientRegistry.Changed += OnClientRegistryChanged;

            _tppaBridge = new TppaSerialBridge(_mount);
            // PA toggle: refresh MountStateCache live (no reconnect) + rebind
            // bridge. EnsureTppaBridgePortDefaulted() picks up freshly-installed
            // com0com pairs on first toggle.
            try
            {
                Advanced.SetPolarAlignmentChangeHandler(() =>
                {
                    try { DriverSettings.EnsureTppaBridgePortDefaulted(); } catch { }
                    try { _mount.State?.RefreshPolarAlignmentMode(); } catch (Exception ex) { TransportLogger.Note("PA refresh failed: " + ex.Message); }
                    ReconcileTppaBridge();
                });
            }
            catch { }

            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _pollTimer.Tick += (s, e) => OnTick();
            _pollTimer.Start();

            UpdateClientLabel();

            if (DriverSettings.CheckUpdatesOnStartup)
            {
                try { Update.StartStartupCheck(); } catch { }
            }
        }

        public void Detach()
        {
            try { _pollTimer?.Stop(); } catch { }
            _mount.ConnectionChanged -= OnMountConnectionChanged;
            _mount.LimitWarning -= OnMountLimitWarning;
            ClientRegistry.Changed -= OnClientRegistryChanged;
            try { _tppaBridge?.Dispose(); } catch { }
            Console.Detach();
        }

        // Settings UI calls this after Apply so a port-name change or PA-mode
        // toggle takes effect without waiting for the next connection event.
        public void ReconcileTppaBridge()
        {
            try { _tppaBridge?.Reconcile(); }
            catch (Exception ex) { TransportLogger.Note("TPPA bridge reconcile failed: " + ex.Message); }
        }

        public void TryAutoConnect()
        {
            if (_hubConnected || _mount.IsOpen) return;
            if (!DriverSettings.AutoConnect) return;
            if (!Connection.HasUsablePortSetting()) return;
            Connection.DoConnect();
        }

        // Called from ConnectionViewModel after a successful Open() returns.
        public void OnConnected()
        {
            _hubConnected = true;
            Tracking.OnConnected(Connection.MountBaseSlewDegPerSec, Connection.MountUsPerStepBase);
            Tracking.SaveOnConnect();
            State = ConnState.Connected;
            ReconcileSiteLocationOnConnect();
            if (DateTime.AutoSyncOnConnect)
            {
                try { DateTime.DoSyncTime(); TransportLogger.Note("Auto-synced date/time from PC on connect"); }
                catch (Exception ex) { TransportLogger.Note("Auto-sync time failed: " + ex.Message); }
            }
            ReapplyAdvancedSettingsOnConnect();
            try { Focuser.OnConnected(); }
            catch (Exception ex) { TransportLogger.Note("Focuser OnConnected failed: " + ex.Message); }
            try { Rotator.OnConnected(); }
            catch (Exception ex) { TransportLogger.Note("Rotator OnConnected failed: " + ex.Message); }
            try { PolarAlignment.OnConnected(); }
            catch (Exception ex) { TransportLogger.Note("PolarAlignment OnConnected failed: " + ex.Message); }
            try { MountSettings.OnConnected(); }
            catch (Exception ex) { TransportLogger.Note("MountSettings OnConnected failed: " + ex.Message); }
        }

        public void SetState(ConnState s)
        {
            if (s != ConnState.Connected) _hubConnected = false;
            State = s;
            if (s == ConnState.Disconnected)
            {
                Position.OnDisconnected();
                Tracking.OnDisconnected();
                Visualizer.OnDisconnected();
                Focuser.OnDisconnected();
                Rotator.OnDisconnected();
                PolarAlignment.OnDisconnected();
            }
        }

        // Mirror of HubForm.SyncConnectionStateFromMount, marshalled to UI thread.
        private void OnMountConnectionChanged(object sender, EventArgs e)
        {
            try
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    bool open = _mount.IsOpen;
                    if (_state == ConnState.Connecting)
                    {
                        if (open && !_hubConnected)
                        {
                            _hubConnected = true;
                            State = ConnState.Connected;
                        }
                        try { _tppaBridge?.Reconcile(); } catch { }
                        return;
                    }
                    if (open == _hubConnected) { try { _tppaBridge?.Reconcile(); } catch { } return; }
                    _hubConnected = open;
                    State = open ? ConnState.Connected : ConnState.Disconnected;
                    try { _tppaBridge?.Reconcile(); } catch { }
                }));
            }
            catch { }
        }

        private void OnMountLimitWarning(string reason)
        {
            try
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    Limits.OnMountLimitWarning(reason)));
            }
            catch { }
        }

        private void OnClientRegistryChanged(object sender, EventArgs e)
        {
            try { Application.Current?.Dispatcher.BeginInvoke(new Action(UpdateClientLabel)); }
            catch { }
        }

        private void UpdateClientLabel()
        {
            int n = ClientRegistry.Count;
            ClientCountText = "Connected clients: " + n;
            ClientCountKind = n > 0 ? StatusKind.Ok : StatusKind.Info;
            ClientCountPulse = n > 0;
        }

        // 250 ms tick. Picks up MountStateCache snapshot, fans out to VMs,
        // and refreshes the local-time display.
        private void OnTick()
        {
            // Always tick the local clock so the displayed seconds advance even
            // while disconnected.
            DateTime.Tick(false, false);

            if (!_hubConnected) return;
            var st = _mount.State;
            if (st == null) return;
            Position.OnPollSnapshot(st);
            Tracking.OnPollSnapshot(st);
            Limits.OnPollSnapshot(st);
            Visualizer.OnPollSnapshot(st);
            Focuser.OnPollSnapshot(st);
            Rotator.OnPollSnapshot(st);
            PolarAlignment.OnPollSnapshot(st);
        }

        // Compare hub-stored site with mount site after connect, prompt if differ.
        // Ports HubForm.ReconcileSiteLocationOnConnect.
        private void ReconcileSiteLocationOnConnect()
        {
            double hubLat = DriverSettings.SiteLatitude;
            double hubLon = DriverSettings.SiteLongitude;
            double hubEle = DriverSettings.SiteElevation;

            double mLat = 0, mLon = 0, mEle = 0;
            string latRaw = "", lonRaw = "";
            bool ok = false;
            Exception lastEx = null;
            for (int attempt = 0; attempt < 3 && !ok; attempt++)
            {
                try
                {
                    latRaw = _mount.Protocol.GetLatitude();
                    lonRaw = _mount.Protocol.GetLongitudeRaw();
                    if (!CoordFormat.TryParseDegrees(latRaw, out mLat))
                        throw new FormatException("latitude reply '" + latRaw + "'");
                    if (!CoordFormat.TryParseDegrees(lonRaw, out var westPos))
                        throw new FormatException("longitude reply '" + lonRaw + "'");
                    mLon = -westPos;
                    double.TryParse(StripReply(_mount.Protocol.GetElevation()),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out mEle);
                    ok = true;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    System.Threading.Thread.Sleep(250);
                }
            }
            if (!ok)
            {
                Views.CopyableMessage.Show("Site check",
                    "Could not read site from mount:\r\n\r\n" + (lastEx?.Message ?? "unknown") +
                    "\r\n\r\nRaw latitude reply:  " + FormatReply(latRaw) +
                    "\r\nRaw longitude reply: " + FormatReply(lonRaw) +
                    "\r\n\r\nIf the bytes look like random high-byte values, the baud rate is\r\n" +
                    "likely wrong or the adapter is still pulsing DTR/RTS on open.");
                return;
            }

            const double latLonTolDeg = 1.0 / 60.0;
            const double eleTolM = 10.0;
            bool differs = Math.Abs(mLat - hubLat) > latLonTolDeg
                        || Math.Abs(mLon - hubLon) > latLonTolDeg
                        || Math.Abs(mEle - hubEle) > eleTolM;

            if (!differs)
            {
                Site.ApplyFromMount(mLat, mLon, mEle);
                return;
            }

            string msg =
                "Mount site location differs from the hub.\r\n\r\n" +
                "Mount:\r\n" +
                "   Lat " + CoordFormat.FormatLatitudeDms(mLat) + "\r\n" +
                "   Lon " + CoordFormat.FormatLongitudeDms(mLon) + "\r\n" +
                "   Ele " + mEle.ToString("F1", CultureInfo.InvariantCulture) + " m\r\n\r\n" +
                "Hub:\r\n" +
                "   Lat " + CoordFormat.FormatLatitudeDms(hubLat) + "\r\n" +
                "   Lon " + CoordFormat.FormatLongitudeDms(hubLon) + "\r\n" +
                "   Ele " + hubEle.ToString("F1", CultureInfo.InvariantCulture) + " m\r\n\r\n" +
                "Yes  = use Mount location (update hub)\r\n" +
                "No   = use Hub location (write to mount)\r\n" +
                "Cancel = keep both as-is";

            var r = MessageBox.Show(msg, "Site location mismatch",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes)
            {
                Site.ApplyFromMount(mLat, mLon, mEle);
                DriverSettings.SiteLatitude = mLat;
                DriverSettings.SiteLongitude = mLon;
                DriverSettings.SiteElevation = mEle;
            }
            else if (r == MessageBoxResult.No)
            {
                try
                {
                    _mount.Protocol.SetLatitude(hubLat);
                    _mount.Protocol.SetLongitude(hubLon);
                    _mount.Protocol.SetElevation(hubEle);
                }
                catch (Exception ex)
                {
                    Views.CopyableMessage.Show("Site sync", "Failed to write site to mount:\r\n\r\n" + ex.ToString());
                }
            }
        }

        private void ReapplyAdvancedSettingsOnConnect()
        {
            try
            {
                var pref = DriverSettings.PreferredPierSide;
                LX200Protocol.PreferredPier pierEnum;
                switch (string.IsNullOrEmpty(pref) ? 'B' : char.ToUpperInvariant(pref[0]))
                {
                    case 'E': pierEnum = LX200Protocol.PreferredPier.East; break;
                    case 'W': pierEnum = LX200Protocol.PreferredPier.West; break;
                    case 'A': pierEnum = LX200Protocol.PreferredPier.Auto; break;
                    default:  pierEnum = LX200Protocol.PreferredPier.Best; break;
                }
                _mount.Protocol.SetPreferredPierSide(pierEnum);
                _mount.Protocol.SetPauseAtHomeOnFlip(DriverSettings.PauseAtHomeOnFlip);
                TransportLogger.Note("Re-applied advanced settings (preferred pier=" + pref + ", pauseHome=" + DriverSettings.PauseAtHomeOnFlip + ")");
            }
            catch (Exception ex)
            {
                TransportLogger.Note("Re-apply advanced settings failed: " + ex.Message);
            }
        }

        private static string StripReply(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.TrimEnd('#').Trim();
        }

        private static string FormatReply(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(empty)";
            var sb = new System.Text.StringBuilder();
            sb.Append("'");
            foreach (char c in s) sb.Append(c >= 0x20 && c < 0x7F ? c : '.');
            sb.Append("'  hex: ");
            foreach (char c in s) sb.AppendFormat("{0:X2} ", (int)c & 0xFF);
            return sb.ToString().TrimEnd();
        }

        private static string GetVersionString()
        {
            try
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                if (v == null) return "v0.0.0";
                return "v" + v.Major + "." + v.Minor + "." + v.Build;
            }
            catch { return "v0.0.0"; }
        }
    }
}
