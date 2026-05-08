using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Input;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Diagnostics;
using ASCOM.OnStepX.Hardware;
using ASCOM.OnStepX.Hardware.State;

namespace ASCOM.OnStepX.ViewModels
{
    // Rotator section. Backs the ROTATOR card in MainWindow.xaml. AXIS3 in
    // OnStepX firmware. The wire format is DMS (sDDD*MM); this VM displays
    // angles as decimal degrees and lets the user issue gotos in 0..360.
    //
    // Static state (capability, limits, step size, backlash) is read at probe
    // time in MountStateCache. Live state (angle, moving, derot flags) rides
    // along on the slow-cadence (~3 s) rotator poll.
    public sealed class RotatorViewModel : ViewModelBase
    {
        private readonly MainViewModel _main;
        private readonly MountSession _mount = MountSession.Instance;

        // Move band: 1..4 (jog rates 0.01°/s … 0.5×goto). Goto band: 5..9
        // (0.5× … 2× base). Same RateOption record FocuserViewModel defines
        // is reused below.
        public ObservableCollection<RateOption> MoveRateOptions { get; } = new ObservableCollection<RateOption>
        {
            new RateOption(1, "0.01°/s"),
            new RateOption(2, "0.1°/s"),
            new RateOption(3, "1°/s"),
            new RateOption(4, "0.5× goto"),
        };
        public ObservableCollection<RateOption> GotoRateOptions { get; } = new ObservableCollection<RateOption>
        {
            new RateOption(5, "0.5×"),
            new RateOption(6, "0.66×"),
            new RateOption(7, "1×"),
            new RateOption(8, "1.5×"),
            new RateOption(9, "2×"),
        };

        public bool MountActionsEnabled => _main.State == ConnState.Connected && IsAvailable;

        // ---------- Availability / capability ----------
        private bool _isAvailable;
        public bool IsAvailable
        {
            get => _isAvailable;
            private set
            {
                if (!Set(ref _isAvailable, value)) return;
                OnPropertyChanged(nameof(MountActionsEnabled));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private string _capability = "";
        public string Capability
        {
            get => _capability;
            private set
            {
                if (!Set(ref _capability, value ?? "")) return;
                OnPropertyChanged(nameof(IsDerotateCapable));
            }
        }
        public bool IsDerotateCapable => string.Equals(Capability, "D", StringComparison.OrdinalIgnoreCase);

        // ---------- Status badge ----------
        private string _statusText = "Idle";
        public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
        private StatusKind _statusKind = StatusKind.Neutral;
        public StatusKind StatusKind { get => _statusKind; private set => Set(ref _statusKind, value); }
        private bool _statusPulse;
        public bool StatusPulse { get => _statusPulse; private set => Set(ref _statusPulse, value); }

        // ---------- Live state ----------
        // Raw firmware angle (mount-native signed range). Display widgets bind
        // to DisplayAngleDeg / AngleText which apply the driver-side sync
        // offset so the Hub and ASCOM clients see the same number.
        private double _angleDeg = double.NaN;
        public double AngleDeg
        {
            get => _angleDeg;
            private set
            {
                if (!Set(ref _angleDeg, value)) return;
                OnPropertyChanged(nameof(DisplayAngleDeg));
                OnPropertyChanged(nameof(DisplayAngleDegSafe));
                OnPropertyChanged(nameof(AngleText));
            }
        }

        // Mirror of DriverSettings.RotatorSyncOffsetDeg, refreshed on connect
        // and after the Restore-Last-Angle command.
        private double _syncOffsetDeg = DriverSettings.RotatorSyncOffsetDeg;

        public double DisplayAngleDeg
        {
            get
            {
                if (double.IsNaN(_angleDeg) || double.IsInfinity(_angleDeg)) return double.NaN;
                return Norm360(_angleDeg + _syncOffsetDeg);
            }
        }
        // Safe variant for the dial's RotateTransform binding — RotateTransform
        // doesn't render meaningfully with NaN, so park the needle at 0° while
        // disconnected.
        public double DisplayAngleDegSafe
        {
            get { double v = DisplayAngleDeg; return double.IsNaN(v) ? 0.0 : v; }
        }
        public string AngleText
        {
            get
            {
                double v = DisplayAngleDeg;
                return double.IsNaN(v) ? "—" : v.ToString("0.00", CultureInfo.InvariantCulture) + "°";
            }
        }

        private bool _moving;
        public bool Moving { get => _moving; private set => Set(ref _moving, value); }

        private bool _derotating;
        public bool Derotating
        {
            get => _derotating;
            private set
            {
                if (!Set(ref _derotating, value)) return;
                _suppressDerotEnabledEvent = true;
                try { DerotEnabled = value; }
                finally { _suppressDerotEnabledEvent = false; }
            }
        }

        private bool _derotReversed;
        public bool DerotReversed { get => _derotReversed; private set => Set(ref _derotReversed, value); }

        // ---------- User input ----------
        private double _targetDeg;
        public double TargetDeg { get => _targetDeg; set => Set(ref _targetDeg, value); }

        private int _moveRatePreset = DriverSettings.RotatorMoveRatePreset;
        public int MoveRatePreset
        {
            get => _moveRatePreset;
            set
            {
                if (value < 1 || value > 4) return;
                if (!Set(ref _moveRatePreset, value)) return;
                DriverSettings.RotatorMoveRatePreset = value;
                if (_main.State != ConnState.Connected || !IsAvailable) return;
                RunBg(() => _mount.Protocol.SetRotatorMoveRatePreset(value));
            }
        }

        private int _gotoRatePreset = DriverSettings.RotatorGotoRatePreset;
        public int GotoRatePreset
        {
            get => _gotoRatePreset;
            set
            {
                if (value < 5 || value > 9) return;
                if (!Set(ref _gotoRatePreset, value)) return;
                DriverSettings.RotatorGotoRatePreset = value;
                if (_main.State != ConnState.Connected || !IsAvailable) return;
                RunBg(() => _mount.Protocol.SetRotatorGotoRatePreset(value));
            }
        }

        private int _backlash;
        public int Backlash { get => _backlash; set => Set(ref _backlash, value); }

        private int _minDeg;
        public int MinDeg { get => _minDeg; private set => Set(ref _minDeg, value); }
        private int _maxDeg;
        public int MaxDeg { get => _maxDeg; private set => Set(ref _maxDeg, value); }

        private double _stepSizeDeg;
        public double StepSizeDeg
        {
            get => _stepSizeDeg;
            private set
            {
                if (!Set(ref _stepSizeDeg, value)) return;
                OnPropertyChanged(nameof(StepSizeText));
            }
        }
        public string StepSizeText => StepSizeDeg > 0
            ? StepSizeDeg.ToString("0.00000", CultureInfo.InvariantCulture) + "°"
            : "—";

        // Derotator panel — only visible when Capability == "D".
        private bool _derotEnabled;
        public bool DerotEnabled
        {
            get => _derotEnabled;
            set
            {
                if (!Set(ref _derotEnabled, value)) return;
                if (_suppressDerotEnabledEvent || _main.State != ConnState.Connected || !IsAvailable) return;
                RunBg(() => _mount.Protocol.EnableDerotator(value));
            }
        }
        private bool _suppressDerotEnabledEvent;

        // Restore-last-angle UX. Saved on every Nth poll while connected; on
        // reconnect, OnConnected loads it so the user can apply a one-click
        // sync if firmware came up at 0° after a power cycle (firmware NV
        // only persists position via park()).
        private double _lastSavedAngleDeg = double.NaN;
        public double LastSavedAngleDeg
        {
            get => _lastSavedAngleDeg;
            private set
            {
                if (!Set(ref _lastSavedAngleDeg, value)) return;
                OnPropertyChanged(nameof(LastSavedAngleText));
                OnPropertyChanged(nameof(RestoreVisible));
            }
        }
        public string LastSavedAngleText => double.IsNaN(LastSavedAngleDeg)
            ? ""
            : LastSavedAngleDeg.ToString("0.00", CultureInfo.InvariantCulture) + "°";
        // Latched on connect by EvaluateRestorePending — true means firmware
        // came up at ~0° while the registry holds a real prior position
        // (cold-boot signature). Stays true until the user clicks Restore,
        // Dismiss, or starts moving the rotator. Polling does NOT re-evaluate
        // (otherwise the card flickers off as soon as the user nudges past 0°).
        private bool _restorePending;
        public bool RestoreVisible => _restorePending
            && IsAvailable
            && !double.IsNaN(LastSavedAngleDeg);
        private int _saveTickCounter; // throttle registry writes

        // ---------- Commands ----------
        public ICommand GotoCommand               { get; }
        public ICommand JogCwCommand              { get; }
        public ICommand JogCcwCommand             { get; }
        public ICommand HaltCommand               { get; }
        public ICommand ZeroCommand               { get; }
        public ICommand HalfTravelCommand         { get; }
        public ICommand GoHomeCommand             { get; }
        public ICommand ApplyBacklashCommand      { get; }
        public ICommand ToggleDerotReverseCommand { get; }
        public ICommand ParallacticCommand        { get; }
        public ICommand RestoreLastAngleCommand   { get; }
        public ICommand DismissRestoreCommand     { get; }
        public ICommand OpenAdvancedCommand       { get; }

        public RotatorViewModel(MainViewModel main)
        {
            _main = main;
            // Every command body runs on the threadpool — pipe round-trips can
            // block 100s of ms (poll loop arbitration + serial), and we don't
            // want the WPF UI thread to freeze on a button click.
            GotoCommand               = new RelayCommand(DoGoto,             () => MountActionsEnabled);
            JogCwCommand              = new RelayCommand(DoJogCw,            () => MountActionsEnabled);
            JogCcwCommand             = new RelayCommand(DoJogCcw,           () => MountActionsEnabled);
            HaltCommand               = new RelayCommand(() => GuardBg(() => _mount.Protocol.RotatorHalt()),         () => MountActionsEnabled);
            ZeroCommand               = new RelayCommand(() => GuardBg(() => _mount.Protocol.RotatorZero()),         () => MountActionsEnabled);
            HalfTravelCommand         = new RelayCommand(() => GuardBg(() => _mount.Protocol.RotatorSetHalfTravel()),() => MountActionsEnabled);
            GoHomeCommand             = new RelayCommand(() => GuardBg(() => _mount.Protocol.RotatorGoHome()),       () => MountActionsEnabled);
            ApplyBacklashCommand      = new RelayCommand(DoApplyBacklash,    () => MountActionsEnabled);
            ToggleDerotReverseCommand = new RelayCommand(() => GuardBg(() => _mount.Protocol.RotatorReverseToggle()),() => MountActionsEnabled && IsDerotateCapable);
            ParallacticCommand        = new RelayCommand(() => GuardBg(() => _mount.Protocol.RotatorGotoParallactic()),() => MountActionsEnabled && IsDerotateCapable);
            RestoreLastAngleCommand   = new RelayCommand(DoRestoreLastAngle, () => MountActionsEnabled && RestoreVisible);
            DismissRestoreCommand     = new RelayCommand(() =>
            {
                _restorePending = false;
                LastSavedAngleDeg = double.NaN;
                DriverSettings.RotatorLastAngleDeg = double.NaN;
                OnPropertyChanged(nameof(RestoreVisible));
            });
            OpenAdvancedCommand       = new RelayCommand(OpenAdvanced);
        }

        private void OpenAdvanced()
        {
            var dlg = new Views.RotatorAdvancedWindow(this)
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };
            dlg.ShowDialog();
        }

        internal void OnConnStateChanged()
        {
            OnPropertyChanged(nameof(MountActionsEnabled));
            CommandManager.InvalidateRequerySuggested();
        }

        public void OnConnected()
        {
            var st = _mount.State;
            if (st == null) return;
            IsAvailable = st.RotatorAvailable;
            if (!IsAvailable) return;
            Capability    = st.RotatorCapability;
            MinDeg        = st.RotatorMinDeg;
            MaxDeg        = st.RotatorMaxDeg;
            StepSizeDeg   = st.RotatorStepSizeDeg;
            Backlash      = st.RotatorBacklashSteps;
            LastSavedAngleDeg = DriverSettings.RotatorLastAngleDeg;
            // Pick up any sync offset another client / earlier session set.
            _syncOffsetDeg = DriverSettings.RotatorSyncOffsetDeg;
            OnPropertyChanged(nameof(DisplayAngleDeg));
            OnPropertyChanged(nameof(DisplayAngleDegSafe));
            OnPropertyChanged(nameof(AngleText));
            // Reapply the user's preferred firmware-side rates — these are
            // session state in the firmware, lost across power cycles.
            RunBg(() =>
            {
                _mount.Protocol.SetRotatorMoveRatePreset(_moveRatePreset);
                _mount.Protocol.SetRotatorGotoRatePreset(_gotoRatePreset);
            });
            // Default goto target to current angle so an accidental Goto click
            // doesn't yank the rotator to 0. Same read evaluates the cold-boot
            // signature for the Restore card — done once here so polling later
            // can't make the card flicker.
            RunBg(() =>
            {
                double cur = _mount.Protocol.GetRotatorAngleDeg();
                if (double.IsNaN(cur)) return;
                bool coldBootLike = Math.Abs(Norm360(cur)) < 0.5
                                 || Math.Abs(Norm360(cur) - 360) < 0.5;
                bool haveSaved = !double.IsNaN(LastSavedAngleDeg)
                              && Math.Abs(LastSavedAngleDeg) >= 0.5;
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    TargetDeg = Norm360(cur);
                    _restorePending = coldBootLike && haveSaved;
                    OnPropertyChanged(nameof(RestoreVisible));
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                });
            });
        }

        public void OnDisconnected()
        {
            // Last-chance save: capture the most recent displayed angle to the
            // registry before tearing down. Covers the "moved-then-power-off"
            // race where the throttled poll-tick save hadn't yet fired. Skip
            // when the Restore card was still pending — overwriting the user's
            // unresolved saved value with a cold-boot 0° defeats the point.
            if (!_restorePending && !double.IsNaN(_angleDeg))
            {
                double v = DisplayAngleDeg;
                if (!double.IsNaN(v))
                {
                    DriverSettings.RotatorLastAngleDeg = v;
                    LastSavedAngleDeg = v;
                }
            }

            IsAvailable = false;
            Moving = false;
            Derotating = false;
            DerotReversed = false;
            AngleDeg = double.NaN;
            StatusKind = StatusKind.Neutral;
            StatusText = "Idle";
            StatusPulse = false;
            // Card is connection-scoped — never carry pending state into the
            // next session. OnConnected re-evaluates on a fresh :rG#.
            _restorePending = false;
            OnPropertyChanged(nameof(RestoreVisible));
        }

        internal void OnPollSnapshot(MountStateCache st)
        {
            if (!st.RotatorAvailable) { if (IsAvailable) OnDisconnected(); return; }

            // Late detection: cache's lazy re-probe just succeeded after our
            // OnConnected gave up. Pick up the static fields the same way.
            if (!IsAvailable)
            {
                IsAvailable   = true;
                Capability    = st.RotatorCapability;
                MinDeg        = st.RotatorMinDeg;
                MaxDeg        = st.RotatorMaxDeg;
                StepSizeDeg   = st.RotatorStepSizeDeg;
                Backlash      = st.RotatorBacklashSteps;
                try
                {
                    double cur = _mount.Protocol.GetRotatorAngleDeg();
                    if (!double.IsNaN(cur)) TargetDeg = Norm360(cur);
                }
                catch { }
            }

            AngleDeg = st.RotatorAngleDeg;
            Moving = st.RotatorMoving;
            Derotating = st.RotatorDerotating;
            DerotReversed = st.RotatorDerotReversed;

            // Any motion (manual jog, NINA goto, derotator slew) means the
            // user has moved past the cold-boot reference — drop the Restore
            // card. They can still reopen it after a fresh reconnect if needed.
            if (_restorePending && Moving)
            {
                _restorePending = false;
                OnPropertyChanged(nameof(RestoreVisible));
            }

            // Persist the live displayed angle. Save immediately when the
            // current value drifts >0.5° from the last saved (covers fast
            // disconnect-after-move where a 30-second heartbeat would miss
            // the new position). A heartbeat every ~30 s also writes through
            // for slow drift / clock-skew sanity.
            //
            // Crucially blocked while _restorePending is true — otherwise the
            // first poll after a cold-boot connect would overwrite the saved
            // 15° with the firmware's freshly-zeroed reading and the user
            // would never see what they're trying to restore.
            if (!Moving && !_restorePending && !double.IsNaN(AngleDeg))
            {
                double v = DisplayAngleDeg;
                if (!double.IsNaN(v))
                {
                    bool changed = double.IsNaN(LastSavedAngleDeg)
                                || Math.Abs(NormSigned(v - LastSavedAngleDeg)) > 0.5;
                    bool heartbeat = (++_saveTickCounter % 10) == 0;
                    if (changed || heartbeat)
                    {
                        DriverSettings.RotatorLastAngleDeg = v;
                        LastSavedAngleDeg = v;
                    }
                }
            }

            if (Moving)             { StatusKind = StatusKind.Info;    StatusText = "Slewing";    StatusPulse = true; }
            else if (Derotating)    { StatusKind = StatusKind.Ok;      StatusText = "Derotating"; StatusPulse = false; }
            else                    { StatusKind = StatusKind.Neutral; StatusText = "Idle";       StatusPulse = false; }
        }

        // ---------- Command bodies ----------
        // All goto / jog / apply paths execute on the threadpool. Errors are
        // marshalled back to the UI thread via the dispatcher before showing
        // a message box.
        private void DoGoto()
        {
            if (!MountActionsEnabled) return;
            double t = Norm360(TargetDeg);
            RunBg(() =>
            {
                _mount.Protocol.SetRotatorGotoRatePreset(_gotoRatePreset);
                double mountTarget = ToMountSigned(t);
                if (!_mount.Protocol.SetRotatorAngleDeg(mountTarget))
                {
                    string err = ""; try { err = _mount.Protocol.GetLastError(); } catch { }
                    ShowOnUi("Goto rejected by mount." +
                        (string.IsNullOrEmpty(err) ? "" : "\r\nMount error: " + err));
                }
            });
        }

        private void DoJogCw()
        {
            if (!MountActionsEnabled) return;
            RunBg(() =>
            {
                _mount.Protocol.SetRotatorMoveRatePreset(_moveRatePreset);
                _mount.Protocol.RotatorJogCw();
            });
        }

        private void DoJogCcw()
        {
            if (!MountActionsEnabled) return;
            RunBg(() =>
            {
                _mount.Protocol.SetRotatorMoveRatePreset(_moveRatePreset);
                _mount.Protocol.RotatorJogCcw();
            });
        }

        private void DoApplyBacklash()
        {
            if (!MountActionsEnabled) return;
            int steps = Backlash;
            RunBg(() =>
            {
                if (!_mount.Protocol.SetRotatorBacklashSteps(steps))
                {
                    string err = ""; try { err = _mount.Protocol.GetLastError(); } catch { }
                    ShowOnUi("Backlash apply rejected." +
                        (string.IsNullOrEmpty(err) ? "" : "\r\nMount error: " + err));
                }
            });
        }

        // Restore = update the apparent position WITHOUT physical motion.
        // OnStepX firmware doesn't persist rotator position to NV across power
        // cycles (only park() does), but the stepper holds its physical pose
        // while powered — so "I parked the camera at 15°, powered off, came
        // back, the camera is still at 15°" is the common case. We can't
        // rewrite the firmware's position counter via LX200 (no command for
        // it), so instead set the driver-side sync offset: ASCOM clients and
        // the Hub display the saved angle, while the firmware counter stays
        // at 0. Subsequent gotos translate through the offset and produce
        // the right physical motion.
        private void DoRestoreLastAngle()
        {
            if (double.IsNaN(LastSavedAngleDeg)) return;
            double saved = LastSavedAngleDeg;
            RunBg(() =>
            {
                double raw = _mount.Protocol.GetRotatorAngleDeg();
                if (double.IsNaN(raw)) { ShowOnUi("Rotator: cannot read current angle."); return; }
                double offset = NormSigned(saved - Norm360(raw));
                DriverSettings.RotatorSyncOffsetDeg = offset;
                DebugLogger.Log("ROTATOR",
                    "Restore (sync offset): saved=" + saved.ToString("0.00", CultureInfo.InvariantCulture) +
                    " raw=" + raw.ToString("0.00", CultureInfo.InvariantCulture) +
                    " offset=" + offset.ToString("0.00", CultureInfo.InvariantCulture));
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _syncOffsetDeg = offset;
                    _restorePending = false;
                    OnPropertyChanged(nameof(DisplayAngleDeg));
                    OnPropertyChanged(nameof(DisplayAngleDegSafe));
                    OnPropertyChanged(nameof(AngleText));
                    OnPropertyChanged(nameof(RestoreVisible));
                }));
            });
        }

        private void GuardBg(Action a)
        {
            if (!MountActionsEnabled) return;
            RunBg(a);
        }

        private static void RunBg(Action a)
        {
            Task.Run(() =>
            {
                try { a(); }
                catch (Exception ex) { DebugLogger.LogException("ROTATOR", ex); }
            });
        }

        private static void ShowOnUi(string msg)
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    Views.CopyableMessage.Show("Rotator", msg)));
            }
            catch { }
        }

        private static double NormSigned(double deg)
        {
            double r = ((deg % 360.0) + 360.0) % 360.0;
            if (r > 180.0) r -= 360.0;
            return r;
        }

        // 0..360 user input back to firmware's native signed range. If limits
        // look uninitialized (both 0), pass through unchanged.
        private double ToMountSigned(double deg0to360)
        {
            if (MinDeg == 0 && MaxDeg == 0) return deg0to360;
            double s = deg0to360 > 180 ? deg0to360 - 360 : deg0to360;
            if (s < MinDeg) s += 360;
            if (s > MaxDeg) s -= 360;
            if (s < MinDeg) s = MinDeg;
            if (s > MaxDeg) s = MaxDeg;
            return s;
        }

        private static double Norm360(double deg)
        {
            double r = deg % 360.0;
            if (r < 0) r += 360.0;
            return r;
        }
    }
}
