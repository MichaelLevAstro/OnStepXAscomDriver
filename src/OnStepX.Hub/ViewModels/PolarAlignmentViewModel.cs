using System;
using System.Threading.Tasks;
using System.Windows.Input;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Diagnostics;
using ASCOM.OnStepX.Hardware;
using ASCOM.OnStepX.Hardware.State;

namespace ASCOM.OnStepX.ViewModels
{
    // Polar Alignment Wedge section. Drives the two-row jog pad in
    // MainWindow.xaml when MountStateCache.PolarAlignmentMode is true.
    //
    // Wire model: focuser 1 = Alt screw, focuser 2 = Az screw. The OnStepX
    // ":FA[n]#" command uses *focuser index* (1..FocuserCount), NOT physical
    // axis number — when AXIS4 + AXIS5 are enabled in Config.h they become
    // focuser 1 + focuser 2 on the wire. Each click sequence is:
    //   :FA[1|2]#  -> select focuser
    //   :F[5..9]#  -> set goto-rate band (since :Fr# runs on goto rate)
    //   :Fr[±N]#   -> relative move N steps; firmware halts on its own
    // The user-tunable StepSize per axis caps how far one click can move so a
    // mistaken VF tap can't drive the wedge into a hard stop. Stop button
    // sends :FQ# regardless of which focuser is currently selected.
    public sealed class PolarAlignmentViewModel : ViewModelBase
    {
        private readonly MainViewModel _main;
        private readonly MountSession _mount = MountSession.Instance;

        // Map button-speed code (1=Slow, 2=Fast, 3=VeryFast) to OnStepX goto-rate
        // preset. :Fr# runs on the goto-rate register so we use the 5..9 band
        // (matches FocuserViewModel's GotoRateOptions semantics).
        private const int GotoRateSlow     = 5; // 0.5×
        private const int GotoRateFast     = 7; // 1×
        private const int GotoRateVeryFast = 9; // 2×

        public bool MountActionsEnabled => _main.State == ConnState.Connected && IsAvailable;

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

        // Live position readouts (steps). Updated from the 4×-slow PA poll.
        private int _altPosition;
        public int AltPosition { get => _altPosition; private set => Set(ref _altPosition, value); }
        private int _azPosition;
        public int AzPosition { get => _azPosition; private set => Set(ref _azPosition, value); }

        private bool _altMoving;
        public bool AltMoving { get => _altMoving; private set => Set(ref _altMoving, value); }
        private bool _azMoving;
        public bool AzMoving { get => _azMoving; private set => Set(ref _azMoving, value); }

        // Per-axis click step size. Persisted in registry — survives reconnect.
        private int _altStepSize = DriverSettings.PolarAlignAltStepSize;
        public int AltStepSize
        {
            get => _altStepSize;
            set
            {
                int clamped = Math.Max(1, Math.Min(100000, value));
                if (!Set(ref _altStepSize, clamped)) return;
                DriverSettings.PolarAlignAltStepSize = clamped;
            }
        }

        private int _azStepSize = DriverSettings.PolarAlignAzStepSize;
        public int AzStepSize
        {
            get => _azStepSize;
            set
            {
                int clamped = Math.Max(1, Math.Min(100000, value));
                if (!Set(ref _azStepSize, clamped)) return;
                DriverSettings.PolarAlignAzStepSize = clamped;
            }
        }

        // Speed dropdown (1=Slow, 2=Fast, 3=Very Fast). Shared by both axes.
        public System.Collections.ObjectModel.ObservableCollection<RateOption> SpeedOptions { get; } =
            new System.Collections.ObjectModel.ObservableCollection<RateOption>
            {
                new RateOption(1, "Slow"),
                new RateOption(2, "Fast"),
                new RateOption(3, "Very Fast"),
            };
        private int _selectedSpeed = 1;
        public int SelectedSpeed { get => _selectedSpeed; set => Set(ref _selectedSpeed, Math.Max(1, Math.Min(3, value))); }

        // Per-axis Goto target (motor steps absolute). Bound to NumericBox in
        // the PA section; GotoAlt/Az commands fire :Fs<n># on the chosen axis.
        private int _altGotoTarget;
        public int AltGotoTarget { get => _altGotoTarget; set => Set(ref _altGotoTarget, value); }
        private int _azGotoTarget;
        public int AzGotoTarget { get => _azGotoTarget; set => Set(ref _azGotoTarget, value); }

        public ICommand StopAllCommand { get; }
        public ICommand GotoAltCommand { get; }
        public ICommand GotoAzCommand  { get; }
        public ICommand OpenAdvancedCommand { get; }

        public PolarAlignmentViewModel(MainViewModel main)
        {
            _main = main;
            StopAllCommand = new RelayCommand(DoStopAll, () => _main.State == ConnState.Connected);
            GotoAltCommand = new RelayCommand(() => DoGoto(1, _altGotoTarget), () => MountActionsEnabled);
            GotoAzCommand  = new RelayCommand(() => DoGoto(2, _azGotoTarget),  () => MountActionsEnabled);
            OpenAdvancedCommand = new RelayCommand(OpenAdvanced, () => MountActionsEnabled);
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
            IsAvailable = st.PolarAlignmentMode;
        }

        public void OnDisconnected()
        {
            IsAvailable = false;
            AltPosition = 0;
            AzPosition = 0;
            AltMoving = false;
            AzMoving = false;
        }

        internal void OnPollSnapshot(MountStateCache st)
        {
            if (!st.PolarAlignmentMode) { if (IsAvailable) OnDisconnected(); return; }
            if (!IsAvailable) IsAvailable = true;
            AltPosition = st.Axis4PositionSteps;
            AzPosition  = st.Axis5PositionSteps;
            AltMoving   = st.Axis4Moving;
            AzMoving    = st.Axis5Moving;
        }

        // Called by PolarAlignmentPad on click. focuserIdx ∈ {1, 2} where 1=Alt
        // (axis 4 physical) and 2=Az (axis 5 physical). dirSign ∈ {-1, +1},
        // speedCode ∈ {1, 2, 3} (Slow / Fast / VeryFast).
        public void Jog(int focuserIdx, int dirSign, int speedCode)
        {
            if (!MountActionsEnabled) return;
            if (focuserIdx != 1 && focuserIdx != 2) return;
            if (dirSign != -1 && dirSign != 1) return;
            int rate = speedCode == 1 ? GotoRateSlow
                     : speedCode == 2 ? GotoRateFast
                     : GotoRateVeryFast;
            int step = focuserIdx == 1 ? _altStepSize : _azStepSize;
            int delta = dirSign * step;
            RunBg(() =>
            {
                var st = _mount.State;
                if (st == null) return;
                // Hold PaAxisLock so the poll loop's :FA1#/:FA2# sandwich
                // can't interleave between our select and our move.
                lock (st.PaAxisLock)
                {
                    bool ok = false;
                    try { ok = _mount.Protocol.SetActiveFocuser(focuserIdx); }
                    catch (Exception ex) { DebugLogger.LogException("PA", ex); return; }
                    if (!ok)
                    {
                        DebugLogger.Log("PA", ":FA" + focuserIdx + "# rejected — focuser " + focuserIdx + " not present? Check that AXIS4_DRIVER_MODEL and AXIS5_DRIVER_MODEL are both enabled in firmware Config.h.");
                        return;
                    }
                    // Blind variants — :F[n]# and :Fr<sn># are fire-and-forget
                    // on this firmware; SendAndReceive would block ~1.5s
                    // per command waiting for a reply that never comes,
                    // delaying motor start past NINA's stuck threshold.
                    try { _mount.Protocol.SetFocuserRatePresetBlind(rate); }
                    catch (Exception ex) { DebugLogger.LogException("PA", ex); }
                    try { _mount.Protocol.SetFocuserPositionRelativeStepsBlind(delta); }
                    catch (Exception ex) { DebugLogger.LogException("PA", ex); return; }
                    DebugLogger.Log("PA",
                        "focuser=" + focuserIdx + " :FA" + focuserIdx + "# :F" + rate + "# :Fr" +
                        delta.ToString("+0;-0", System.Globalization.CultureInfo.InvariantCulture) + "#");
                }
            });
        }

        // Absolute goto for one axis. Issues :FA[n]# + :F<rate># + :Fs<steps>#
        // under PaAxisLock to avoid colliding with the cache poll.
        private void DoGoto(int focuserIdx, int targetSteps)
        {
            if (!MountActionsEnabled) return;
            if (focuserIdx != 1 && focuserIdx != 2) return;
            int rate = _selectedSpeed == 1 ? GotoRateSlow
                     : _selectedSpeed == 2 ? GotoRateFast
                     : GotoRateVeryFast;
            int target = targetSteps;
            RunBg(() =>
            {
                var st = _mount.State;
                if (st == null) return;
                lock (st.PaAxisLock)
                {
                    bool ok = false;
                    try { ok = _mount.Protocol.SetActiveFocuser(focuserIdx); }
                    catch (Exception ex) { DebugLogger.LogException("PA", ex); return; }
                    if (!ok)
                    {
                        DebugLogger.Log("PA", ":FA" + focuserIdx + "# rejected — focuser not present");
                        return;
                    }
                    try { _mount.Protocol.SetFocuserRatePresetBlind(rate); }
                    catch (Exception ex) { DebugLogger.LogException("PA", ex); }
                    try { _mount.Protocol.SetFocuserPositionStepsBlind(target); }
                    catch (Exception ex) { DebugLogger.LogException("PA", ex); return; }
                    DebugLogger.Log("PA",
                        "goto focuser=" + focuserIdx + " :FA" + focuserIdx + "# :F" + rate + "# :Fs" + target + "#");
                }
            });
        }

        private void OpenAdvanced()
        {
            try
            {
                var dlg = new Views.PolarAlignmentAdvancedWindow(this)
                {
                    Owner = System.Windows.Application.Current?.MainWindow
                };
                dlg.ShowDialog();
            }
            catch (Exception ex) { DebugLogger.LogException("PA", ex); }
        }

        // Apply current/hold settings to firmware. Called from the advanced
        // dialog's Apply button. Persists to DriverSettings + sends to mount.
        public void ApplyDriverCurrents(int altRunMa, int altHoldPct, int azRunMa, int azHoldPct)
        {
            DriverSettings.PolarAlignAltRunCurrent  = altRunMa;
            DriverSettings.PolarAlignAltHoldPercent = altHoldPct;
            DriverSettings.PolarAlignAzRunCurrent   = azRunMa;
            DriverSettings.PolarAlignAzHoldPercent  = azHoldPct;
            RunBg(() =>
            {
                var st = _mount.State;
                if (st == null) return;
                lock (st.PaAxisLock)
                {
                    try { _mount.Protocol.SetAxisRunCurrentMa(4, altRunMa); } catch (Exception ex) { DebugLogger.LogException("PA", ex); }
                    try { _mount.Protocol.SetAxisHoldPercent(4, altHoldPct); } catch (Exception ex) { DebugLogger.LogException("PA", ex); }
                    try { _mount.Protocol.SetAxisRunCurrentMa(5, azRunMa); } catch (Exception ex) { DebugLogger.LogException("PA", ex); }
                    try { _mount.Protocol.SetAxisHoldPercent(5, azHoldPct); } catch (Exception ex) { DebugLogger.LogException("PA", ex); }
                    DebugLogger.Log("PA",
                        "applied currents Alt run=" + altRunMa + "mA hold=" + altHoldPct +
                        "%, Az run=" + azRunMa + "mA hold=" + azHoldPct + "%");
                }
            });
        }

        private void DoStopAll()
        {
            RunBg(() =>
            {
                var st = _mount.State;
                if (st == null) return;
                lock (st.PaAxisLock)
                {
                    try { _mount.Protocol.SetActiveFocuser(1); } catch { }
                    try { _mount.Protocol.FocuserHalt(); } catch { }
                    try { _mount.Protocol.SetActiveFocuser(2); } catch { }
                    try { _mount.Protocol.FocuserHalt(); } catch { }
                    DebugLogger.Log("PA", "stop-all halted focuser 1 (Alt) and focuser 2 (Az)");
                }
            });
        }

        private static void RunBg(Action a)
        {
            Task.Run(() =>
            {
                try { a(); }
                catch (Exception ex) { DebugLogger.LogException("PA", ex); }
            });
        }
    }
}
