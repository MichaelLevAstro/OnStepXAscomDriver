using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Diagnostics;
using ASCOM.OnStepX.Hardware;
using ASCOM.OnStepX.Hardware.Transport;

namespace ASCOM.OnStepX.ViewModels
{
    // Tracking / Slew card. Mirrors HubForm.BuildTrackingGroup tracking-rate combo,
    // guide rate, slew rate slider/numeric two-way, meridian action combo.
    // Keeps the legacy hub's _suppressXxxEvent gating so poll-driven readbacks
    // don't ricochet through the user's edits.
    public sealed class TrackingViewModel : ViewModelBase
    {
        private readonly MainViewModel _main;
        private readonly MountSession _mount = MountSession.Instance;

        public ObservableCollection<string> TrackingModes { get; } = new ObservableCollection<string> { "Sidereal", "Lunar", "Solar", "King" };
        public ObservableCollection<string> MeridianActions { get; } = new ObservableCollection<string> { "Auto Flip", "Stop at Meridian" };

        private bool _trackingEnabled;
        public bool TrackingEnabled
        {
            get => _trackingEnabled;
            set
            {
                if (!Set(ref _trackingEnabled, value)) return;
                if (_suppressTrackingCheckEvent || _main.State != ConnState.Connected) return;
                ApplyTracking(value);
            }
        }
        private bool _suppressTrackingCheckEvent;

        private string _trackingMode;
        public string TrackingMode
        {
            get => _trackingMode;
            set
            {
                if (!Set(ref _trackingMode, value)) return;
                if (_suppressTrackingModeEvent || _main.State != ConnState.Connected) return;
                switch (value)
                {
                    case "Sidereal": _mount.Protocol.SetTrackingSidereal(); break;
                    case "Lunar":    _mount.Protocol.SetTrackingLunar();    break;
                    case "Solar":    _mount.Protocol.SetTrackingSolar();    break;
                    case "King":     _mount.Protocol.SetTrackingKing();     break;
                }
                _trackingModeSetAt = DateTime.UtcNow;
            }
        }
        private bool _suppressTrackingModeEvent;
        internal DateTime _trackingModeSetAt = DateTime.MinValue;

        private double _guideRate;
        public double GuideRate
        {
            get => _guideRate;
            set
            {
                if (!Set(ref _guideRate, Math.Max(0.01, Math.Min(1.0, value)))) return;
                if (_main.State == ConnState.Connected)
                    _mount.Protocol.SetGuideRateMultiplier(_guideRate);
            }
        }

        // Slider + numeric box bound 1:1 (as in HubForm). Single property here drives
        // both controls in XAML — TwoWay binding on each. _suppressSlewSyncEvent
        // becomes irrelevant since both views share _slewRate, but kept as a flag
        // for ApplySlewRateDegPerSec gating during connect-time bound retune.
        public double SlewRateMin { get; private set; } = 0.1;
        public double SlewRateMax { get; private set; } = 10.0;
        private double _slewRate;
        public double SlewRate
        {
            get => _slewRate;
            set
            {
                double v = Math.Max(SlewRateMin, Math.Min(SlewRateMax, value));
                if (!Set(ref _slewRate, v)) return;
                if (_suppressSlewSyncEvent) return;
                if (_main.State == ConnState.Connected) ApplySlewRateDegPerSec(v);
            }
        }
        private bool _suppressSlewSyncEvent;

        private string _meridianAction;
        public string MeridianAction
        {
            get => _meridianAction;
            set
            {
                if (!Set(ref _meridianAction, value)) return;
                if (_suppressMeridianActionEvent) return;
                _meridianActionSetAt = DateTime.UtcNow;
                if (_main.State == ConnState.Connected)
                    _mount.Protocol.SetMeridianAutoFlip(value == "Auto Flip");
            }
        }
        private bool _suppressMeridianActionEvent;
        internal DateTime _meridianActionSetAt = DateTime.MinValue;

        // Mount slew base values, populated post-connect by ConnectionViewModel.
        private double _mountBaseSlewDegPerSec;
        private double _mountUsPerStepBase;

        // State pill (right of the "Tracking enabled" check). Replaces _stateLabel/_slewingBadge.
        private string _stateText = "State: —";
        public string StateText { get => _stateText; private set => Set(ref _stateText, value); }
        private StatusKind _stateKind = StatusKind.Neutral;
        public StatusKind StateKind { get => _stateKind; private set => Set(ref _stateKind, value); }
        private bool _statePulse;
        public bool StatePulse { get => _statePulse; private set => Set(ref _statePulse, value); }

        private bool _isSlewing;
        public bool IsSlewing { get => _isSlewing; private set => Set(ref _isSlewing, value); }
        private string _slewCoord;
        public string SlewCoord { get => _slewCoord; private set => Set(ref _slewCoord, value); }

        public bool MountActionsEnabled => _main.State == ConnState.Connected;
        public bool AdvancedButtonEnabled => true; // dialog opens offline (HubForm comment)

        public ICommand AdvancedCommand { get; }

        public TrackingViewModel(MainViewModel main)
        {
            _main = main;
            AdvancedCommand = new RelayCommand(OpenAdvancedSettings);
            LoadFromSettings();
        }

        private void LoadFromSettings()
        {
            _guideRate = DriverSettings.GuideRateMultiplier;
            _slewRate  = DriverSettings.SlewRateDegPerSec;
            _meridianAction = DriverSettings.MeridianAutoFlip ? "Auto Flip" : "Stop at Meridian";
            OnPropertyChanged(nameof(GuideRate));
            OnPropertyChanged(nameof(SlewRate));
            OnPropertyChanged(nameof(MeridianAction));
        }

        internal void OnConnStateChanged()
        {
            OnPropertyChanged(nameof(MountActionsEnabled));
            CommandManager.InvalidateRequerySuggested();
        }

        // Wired from ConnectionViewModel via MainViewModel.OnConnected.
        public void OnConnected(double mountBaseSlew, double mountUsPerStepBase)
        {
            _mountBaseSlewDegPerSec = mountBaseSlew;
            _mountUsPerStepBase = mountUsPerStepBase;

            // Tighten slew rate range to firmware-clamp [base/2, base*2] when the
            // mount reports a sane base; leave defaults otherwise.
            if (mountBaseSlew > 0.1)
            {
                SlewRateMin = Math.Round(mountBaseSlew * 0.5, 2);
                SlewRateMax = Math.Round(mountBaseSlew * 2.0, 2);
                _suppressSlewSyncEvent = true;
                try
                {
                    if (_slewRate < SlewRateMin) _slewRate = SlewRateMin;
                    if (_slewRate > SlewRateMax) _slewRate = SlewRateMax;
                    OnPropertyChanged(nameof(SlewRateMin));
                    OnPropertyChanged(nameof(SlewRateMax));
                    OnPropertyChanged(nameof(SlewRate));
                }
                finally { _suppressSlewSyncEvent = false; }
            }

            // Push the registry-stored slew rate to the mount on every connect
            // so :ENVRESET'd mounts pick up the user's preferred rate.
            try { ApplySlewRateDegPerSec(_slewRate); }
            catch (Exception ex) { TransportLogger.Note("Apply slew rate on connect failed: " + ex.Message); }

            // Read meridian-flip state back so the combo reflects the mount.
            try
            {
                bool autoFlip = _mount.Protocol.GetMeridianAutoFlip();
                _suppressMeridianActionEvent = true;
                try { MeridianAction = autoFlip ? "Auto Flip" : "Stop at Meridian"; }
                finally { _suppressMeridianActionEvent = false; }
            }
            catch (Exception mex) { TransportLogger.Note("Meridian flip readback failed: " + mex.Message); }
        }

        // Save user-tweakable settings (called on Connect success path).
        public void SaveOnConnect()
        {
            DriverSettings.GuideRateMultiplier = _guideRate;
            DriverSettings.SlewRateDegPerSec   = _slewRate;
            DriverSettings.MeridianAutoFlip    = _meridianAction == "Auto Flip";
        }

        private void ApplyTracking(bool on)
        {
            try
            {
                bool ok = on ? _mount.Protocol.TrackingOn() : _mount.Protocol.TrackingOff();
                if (!ok)
                {
                    string err = "";
                    try { err = _mount.Protocol.GetLastError(); } catch { }
                    string hint = _mount.State != null && _mount.State.AtPark
                        ? "\r\n\r\nMount is parked. Unpark before enabling tracking."
                        : "";
                    MessageBox.Show(
                        "Tracking command rejected by mount." +
                        (string.IsNullOrWhiteSpace(err) ? "" : "\r\nMount error: " + err) +
                        hint,
                        "Tracking", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _suppressTrackingCheckEvent = true;
                    try { TrackingEnabled = !on; }
                    finally { _suppressTrackingCheckEvent = false; }
                }
            }
            catch (Exception ex)
            {
                Views.CopyableMessage.Show("Tracking", "Tracking command failed:\r\n\r\n" + ex.ToString());
            }
        }

        public void ApplySlewRateDegPerSec(double targetDegPerSec)
        {
            if (_main.State != ConnState.Connected) return;
            if (_mountBaseSlewDegPerSec <= 0.1) return;
            if (_mountUsPerStepBase <= 0.1) return;
            if (targetDegPerSec <= 0.0) return;
            double targetUs = _mountUsPerStepBase * (_mountBaseSlewDegPerSec / targetDegPerSec);
            try { _mount.Protocol.SetUsPerStepCurrent(targetUs); }
            catch (Exception ex) { TransportLogger.Note("ApplySlewRate failed: " + ex.Message); }
        }

        // Poll-driven; called from MainViewModel's 250 ms pump.
        internal void OnPollSnapshot(Hardware.State.MountStateCache st)
        {
            string mode;
            StatusKind kind;
            bool pulse = false;
            if (st.AtPark)        { mode = "Parked";   kind = StatusKind.Warn; }
            else if (st.Slewing)  { mode = "Slewing";  kind = StatusKind.Info; }
            else if (st.Tracking) { mode = "Tracking"; kind = StatusKind.Ok; pulse = true; }
            else if (st.AtHome)   { mode = "At Home";  kind = StatusKind.Info; }
            else                  { mode = "Idle";     kind = StatusKind.Neutral; }

            IsSlewing = st.Slewing;
            if (st.Slewing)
                SlewCoord = CoordFormat.FormatHoursHighPrec(st.RightAscension) + " " +
                            CoordFormat.FormatDegreesHighPrec(st.Declination);

            StateText = "State: " + mode;
            StateKind = kind;
            StatePulse = pulse;

            if (_trackingEnabled != st.Tracking)
            {
                _suppressTrackingCheckEvent = true;
                try { TrackingEnabled = st.Tracking; }
                finally { _suppressTrackingCheckEvent = false; }
            }

            // 3 s debounce after a user-initiated change — same as HubForm —
            // so the next poll cycle doesn't snap the combo back before the
            // mount has acknowledged.
            bool modeDebouncing = (DateTime.UtcNow - _trackingModeSetAt).TotalMilliseconds < 3000;
            if (!modeDebouncing && !string.IsNullOrEmpty(st.TrackingMode) && st.TrackingMode != _trackingMode)
            {
                _suppressTrackingModeEvent = true;
                try { TrackingMode = st.TrackingMode; }
                finally { _suppressTrackingModeEvent = false; }
            }

            bool meridianDebouncing = (DateTime.UtcNow - _meridianActionSetAt).TotalMilliseconds < 3000;
            if (!meridianDebouncing)
            {
                string desired = st.AutoMeridianFlip ? "Auto Flip" : "Stop at Meridian";
                if (desired != _meridianAction)
                {
                    _suppressMeridianActionEvent = true;
                    try { MeridianAction = desired; }
                    finally { _suppressMeridianActionEvent = false; }
                }
            }
        }

        public void OnDisconnected()
        {
            StateText = "State: —";
            StateKind = StatusKind.Neutral;
            StatePulse = false;
            IsSlewing = false;
        }

        private void OpenAdvancedSettings()
        {
            var dlg = new Views.AdvancedSettingsWindow(_main)
            {
                Owner = Application.Current?.MainWindow
            };
            dlg.ShowDialog();
        }
    }
}
