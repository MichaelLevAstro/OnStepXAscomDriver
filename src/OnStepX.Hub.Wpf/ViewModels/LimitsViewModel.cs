using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Hardware;
using ASCOM.OnStepX.Hardware.State;

namespace ASCOM.OnStepX.ViewModels
{
    // Limits card. Mirrors HubForm.BuildLimitsGroup + DoWriteLimits + RefreshLimitsStatus.
    // Sticky slew-rejection wins over live alt/HA limit checks for ~10 s.
    public sealed class LimitsViewModel : ViewModelBase
    {
        private readonly MainViewModel _main;
        private readonly MountSession _mount = MountSession.Instance;

        private int _horizonLimit;
        private int _overheadLimit;
        private int _meridianEastMin;
        private int _meridianWestMin;
        private int _syncLimit;

        public int HorizonLimit  { get => _horizonLimit;  set => Set(ref _horizonLimit,  Math.Max(-30, Math.Min(30, value))); }
        public int OverheadLimit { get => _overheadLimit; set => Set(ref _overheadLimit, Math.Max(60, Math.Min(90, value))); }
        public int MeridianEastMin { get => _meridianEastMin; set => Set(ref _meridianEastMin, Math.Max(-270, Math.Min(270, value))); }
        public int MeridianWestMin { get => _meridianWestMin; set => Set(ref _meridianWestMin, Math.Max(-270, Math.Min(270, value))); }
        public int SyncLimit
        {
            get => _syncLimit;
            set
            {
                int v = Math.Max(0, Math.Min(180, value));
                if (Set(ref _syncLimit, v)) { try { DriverSettings.SyncLimitDeg = v; } catch { } }
            }
        }

        public bool MountActionsEnabled => _main.State == ConnState.Connected;
        public ICommand UploadCommand { get; }

        // Header status LED.
        private string _statusText;
        public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
        private StatusKind _statusKind = StatusKind.Neutral;
        public StatusKind StatusKind { get => _statusKind; private set => Set(ref _statusKind, value); }
        private bool _statusVisible;
        public bool StatusVisible { get => _statusVisible; private set => Set(ref _statusVisible, value); }
        private bool _statusPulse;
        public bool StatusPulse { get => _statusPulse; private set => Set(ref _statusPulse, value); }

        // Sticky slew-rejection (raised by MountSession.LimitWarning via MainViewModel).
        private DateTime _limitRejectionUntilUtc = DateTime.MinValue;
        private string _limitRejectionMsg;
        private bool _liveLimitActive;

        public LimitsViewModel(MainViewModel main)
        {
            _main = main;
            UploadCommand = new RelayCommand(DoWriteLimits, () => MountActionsEnabled);
            LoadFromSettings();
        }

        private void LoadFromSettings()
        {
            _horizonLimit    = DriverSettings.HorizonLimitDeg;
            _overheadLimit   = DriverSettings.OverheadLimitDeg;
            _meridianEastMin = DriverSettings.MeridianLimitEastMin;
            _meridianWestMin = DriverSettings.MeridianLimitWestMin;
            _syncLimit       = DriverSettings.SyncLimitDeg;
            OnPropertyChanged(nameof(HorizonLimit));
            OnPropertyChanged(nameof(OverheadLimit));
            OnPropertyChanged(nameof(MeridianEastMin));
            OnPropertyChanged(nameof(MeridianWestMin));
            OnPropertyChanged(nameof(SyncLimit));
        }

        internal void OnConnStateChanged()
        {
            OnPropertyChanged(nameof(MountActionsEnabled));
            CommandManager.InvalidateRequerySuggested();
            if (_main.State != ConnState.Connected)
            {
                StatusText = "—";
                StatusKind = StatusKind.Neutral;
                StatusVisible = false;
                StatusPulse = false;
                _liveLimitActive = false;
                _limitRejectionMsg = null;
                _limitRejectionUntilUtc = DateTime.MinValue;
            }
        }

        // From MountSession.LimitWarning event (10 s sticky).
        public void OnMountLimitWarning(string reason)
        {
            _limitRejectionMsg = reason;
            _limitRejectionUntilUtc = DateTime.UtcNow.AddSeconds(10);
        }

        // From the 250 ms poll pump.
        internal void OnPollSnapshot(MountStateCache st)
        {
            if (st.LastUpdateUtc == DateTime.MinValue)
            {
                StatusText = "—";
                StatusKind = StatusKind.Neutral;
                StatusVisible = false;
                StatusPulse = false;
                _liveLimitActive = false;
                return;
            }

            string reason = null;
            string liveReason = null;

            if (DateTime.UtcNow < _limitRejectionUntilUtc && !string.IsNullOrEmpty(_limitRejectionMsg))
                reason = _limitRejectionMsg;

            if (st.Altitude <= _horizonLimit)
                liveReason = "Below horizon " + st.Altitude.ToString("F1", CultureInfo.InvariantCulture) + "°";

            // Meridian-limit check using the raw mechanical axis 1 angle
            // (:GX42#) instead of LST-RA derivation. Axis 1 is in degrees,
            // 0 at meridian, +west, -east on a GEM with default OnStep config,
            // ±90° at horizon. Limits are stored as minutes of RA — convert
            // to degrees: 1 min RA = 0.25°.
            //
            // The "safe" axis-1 envelope is therefore
            //     [-90 - eastLimitDeg, +90 + westLimitDeg]
            // (user-requested formulation: "between 90 and -90 degrees, plus
            // limits"). Past either edge → flag the violation.
            //
            // Gating: only fire while the mount is actively moving — Slewing
            // or Tracking. A parked or idle mount may sit past these edges
            // (typical OnStep park position is HA≈±6h ⇒ axis≈±90°), but
            // there's nothing to warn about while it's stationary; firmware
            // enforces the wire-side limit on its next slew/track command.
            //
            // Falls back to the legacy LST-RA math if Axis1Deg is NaN
            // (firmware doesn't support :GX42#), preserving behavior on
            // pre-OnStepX builds.
            bool moving = st.Slewing || st.Tracking;
            if (liveReason == null && moving)
            {
                double a1 = st.Axis1Deg;
                double eastLimitDeg = _meridianEastMin / 4.0;
                double westLimitDeg = _meridianWestMin / 4.0;

                if (!double.IsNaN(a1))
                {
                    double lowEdge  = -90.0 - eastLimitDeg;
                    double highEdge = +90.0 + westLimitDeg;
                    if (a1 < lowEdge)
                        liveReason = "Past east meridian limit (axis1 " +
                                     a1.ToString("F1", CultureInfo.InvariantCulture) + "°)";
                    else if (a1 > highEdge)
                        liveReason = "Past west meridian limit (axis1 " +
                                     a1.ToString("F1", CultureInfo.InvariantCulture) + "°)";
                }
                else if (!string.IsNullOrEmpty(st.SideOfPier))
                {
                    // Legacy fallback: LST-RA, pier-side aware.
                    double haHours = st.SiderealTime - st.RightAscension;
                    while (haHours > 12) haHours -= 24;
                    while (haHours < -12) haHours += 24;
                    double haMin = haHours * 60.0;
                    if (st.SideOfPier == "E" && haMin < -_meridianEastMin)
                        liveReason = "Past meridian E " + (-haMin).ToString("F0", CultureInfo.InvariantCulture) + "m";
                    else if (st.SideOfPier == "W" && haMin > _meridianWestMin)
                        liveReason = "Past meridian W " + haMin.ToString("F0", CultureInfo.InvariantCulture) + "m";
                }
            }

            // Edge-trigger MountSession.LimitWarning so notification subsystem
            // toasts even on manual-pad / ASCOM-client bypass paths.
            bool nowInLiveLimit = liveReason != null;
            if (nowInLiveLimit && !_liveLimitActive)
            {
                try { _mount.RaiseLimitWarning(liveReason); } catch { }
            }
            _liveLimitActive = nowInLiveLimit;

            if (reason == null) reason = liveReason;

            if (reason != null)
            {
                StatusVisible = true;
                StatusText = reason;
                StatusKind = StatusKind.Err;
                StatusPulse = true;
            }
            else
            {
                StatusPulse = false;
                StatusVisible = false;
            }
        }

        private void DoWriteLimits()
        {
            if (_main.State != ConnState.Connected) return;
            try
            {
                bool hOk = _mount.Protocol.SetHorizonLimit(_horizonLimit);
                bool oOk = _mount.Protocol.SetOverheadLimit(_overheadLimit);
                bool eOk = _mount.Protocol.SetMeridianLimitEastMinutes(_meridianEastMin);
                bool wOk = _mount.Protocol.SetMeridianLimitWestMinutes(_meridianWestMin);
                DriverSettings.HorizonLimitDeg = _horizonLimit;
                DriverSettings.OverheadLimitDeg = _overheadLimit;
                DriverSettings.MeridianLimitEastMin = _meridianEastMin;
                DriverSettings.MeridianLimitWestMin = _meridianWestMin;
                if (!hOk || !oOk || !eOk || !wOk)
                {
                    string err = "";
                    try { err = _mount.Protocol.GetLastError(); } catch { }
                    Views.CopyableMessage.Show("Upload limits",
                        "Mount rejected one or more values.\r\n" +
                        "  Horizon:       " + (hOk ? "OK" : "REJECTED") + "\r\n" +
                        "  Overhead:      " + (oOk ? "OK" : "REJECTED") + "\r\n" +
                        "  Meridian east: " + (eOk ? "OK" : "REJECTED") + "\r\n" +
                        "  Meridian west: " + (wOk ? "OK" : "REJECTED") +
                        (string.IsNullOrWhiteSpace(err) ? "" : "\r\n\r\nMount error: " + err));
                    return;
                }
                MessageBox.Show("Limits uploaded to mount.", "Upload limits", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Views.CopyableMessage.Show("Upload limits", "Upload failed:\r\n\r\n" + ex.ToString());
            }
        }
    }
}
