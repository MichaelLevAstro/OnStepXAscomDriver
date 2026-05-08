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
    // Focuser section. Backs the FOCUSER card in MainWindow.xaml. The Hub UI
    // is the single source of truth for which firmware focuser is selected
    // (`:FA[n]#`); the ASCOM driver always issues bare `:F…#` commands.
    //
    // State that's NV-backed by the firmware (min/max/backlash/TCF coeff) is
    // read on connect and on focuser-switch, and written through on edit.
    // Position / moving / temperature ride along on MountStateCache's
    // slow-cadence (~3 s) focuser poll.
    public sealed class FocuserViewModel : ViewModelBase
    {
        private readonly MainViewModel _main;
        private readonly MountSession _mount = MountSession.Instance;

        public ObservableCollection<int> FocuserOptions { get; } = new ObservableCollection<int>();

        // OnStepX preset rate registers split into move (1..4) vs goto (5..9).
        // The Hub uses step-based manual moves (:Fr[±N]#) which run on the
        // goto-rate register, so we only expose the goto band.
        public ObservableCollection<RateOption> GotoRateOptions { get; } = new ObservableCollection<RateOption>
        {
            new RateOption(5, "0.5×"),
            new RateOption(6, "0.66×"),
            new RateOption(7, "1×"),
            new RateOption(8, "1.5×"),
            new RateOption(9, "2×"),
        };

        public bool MountActionsEnabled => _main.State == ConnState.Connected && IsAvailable;

        // ---------- Availability / selection ----------
        private bool _isAvailable;
        public bool IsAvailable
        {
            get => _isAvailable;
            private set
            {
                if (!Set(ref _isAvailable, value)) return;
                OnPropertyChanged(nameof(MountActionsEnabled));
                OnPropertyChanged(nameof(FocuserSelectionVisible));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private int _count;
        public int Count
        {
            get => _count;
            private set
            {
                if (!Set(ref _count, value)) return;
                FocuserOptions.Clear();
                for (int i = 1; i <= value; i++) FocuserOptions.Add(i);
                OnPropertyChanged(nameof(FocuserSelectionVisible));
            }
        }
        public bool FocuserSelectionVisible => _count > 1;

        private int _activeIndex = 1;
        public int ActiveIndex
        {
            get => _activeIndex;
            set
            {
                if (value < 1 || value > Math.Max(1, _count)) return;
                if (!Set(ref _activeIndex, value)) return;
                if (_suppressActiveIndexEvent || _main.State != ConnState.Connected) return;
                DriverSettings.FocuserDefaultIndex = value;
                int idx = value;
                RunBg(() =>
                {
                    _mount.Protocol.SetActiveFocuser(idx);
                    DebugLogger.Log("FOCUSER", "active -> " + idx);
                    RefreshNvBackedStateBg();
                });
            }
        }
        private bool _suppressActiveIndexEvent;

        // ---------- Live state ----------
        private string _statusText = "Idle";
        public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
        private StatusKind _statusKind = StatusKind.Neutral;
        public StatusKind StatusKind { get => _statusKind; private set => Set(ref _statusKind, value); }
        private bool _statusPulse;
        public bool StatusPulse { get => _statusPulse; private set => Set(ref _statusPulse, value); }

        private int _position;
        public int Position { get => _position; private set => Set(ref _position, value); }

        private string _positionMm = "—";
        public string PositionMm { get => _positionMm; private set => Set(ref _positionMm, value); }

        private bool _moving;
        public bool Moving { get => _moving; private set => Set(ref _moving, value); }

        private string _temperatureText = "—";
        public string TemperatureText { get => _temperatureText; private set => Set(ref _temperatureText, value); }

        // ---------- User input ----------
        private int _targetPosition;
        public int TargetPosition { get => _targetPosition; set => Set(ref _targetPosition, value); }

        private int _gotoRatePreset = 7; // 1× base goto rate
        public int GotoRatePreset
        {
            get => _gotoRatePreset;
            set
            {
                if (value < 5 || value > 9) return;
                if (!Set(ref _gotoRatePreset, value)) return;
                if (_main.State != ConnState.Connected || !IsAvailable) return;
                int rate = value;
                RunBg(() => _mount.Protocol.SetFocuserRatePreset(rate));
            }
        }

        // Per-click step size for In/Out buttons. Persisted to registry.
        private int _stepSize = DriverSettings.FocuserStepSize;
        public int StepSize
        {
            get => _stepSize;
            set
            {
                int clamped = Math.Max(1, Math.Min(100000, value));
                if (!Set(ref _stepSize, clamped)) return;
                DriverSettings.FocuserStepSize = clamped;
            }
        }

        private int _backlash;
        public int Backlash { get => _backlash; set => Set(ref _backlash, value); }

        private int _minSteps;
        public int MinSteps { get => _minSteps; private set => Set(ref _minSteps, value); }
        private int _maxSteps;
        public int MaxSteps { get => _maxSteps; private set => Set(ref _maxSteps, value); }

        private double _stepSizeMicrons;
        public double StepSizeMicrons { get => _stepSizeMicrons; private set => Set(ref _stepSizeMicrons, value); }

        private bool _tempCompAvailable;
        public bool TempCompAvailable { get => _tempCompAvailable; private set => Set(ref _tempCompAvailable, value); }

        private bool _tempCompEnabled;
        public bool TempCompEnabled
        {
            get => _tempCompEnabled;
            set
            {
                if (!Set(ref _tempCompEnabled, value)) return;
                if (_suppressTcfEvent || _main.State != ConnState.Connected || !IsAvailable) return;
                bool on = value;
                RunBg(() => _mount.Protocol.SetFocuserTcfEnabled(on));
            }
        }
        private bool _suppressTcfEvent;

        private double _tcfCoeff;
        public double TcfCoeff { get => _tcfCoeff; set => Set(ref _tcfCoeff, value); }

        private int _tcfDeadband;
        public int TcfDeadband { get => _tcfDeadband; set => Set(ref _tcfDeadband, value); }

        // ---------- Commands ----------
        public ICommand GotoCommand    { get; }
        public ICommand HaltCommand    { get; }
        public ICommand MoveInCommand  { get; }
        public ICommand MoveOutCommand { get; }
        public ICommand SetHomeCommand { get; }
        public ICommand GoHomeCommand  { get; }
        public ICommand ZeroCommand    { get; }
        public ICommand ApplyBacklashCommand { get; }
        public ICommand ApplyTcfCommand      { get; }
        public ICommand OpenAdvancedCommand  { get; }

        public FocuserViewModel(MainViewModel main)
        {
            _main = main;
            // All command bodies dispatch to the threadpool — pipe round-trips
            // can block 100s of ms (poll loop arbitration + serial wire) and
            // we don't want the WPF UI thread to freeze on a button click.
            GotoCommand          = new RelayCommand(DoGoto,    () => MountActionsEnabled);
            HaltCommand          = new RelayCommand(DoHalt,    () => MountActionsEnabled);
            MoveInCommand        = new RelayCommand(DoMoveIn,  () => MountActionsEnabled);
            MoveOutCommand       = new RelayCommand(DoMoveOut, () => MountActionsEnabled);
            SetHomeCommand       = new RelayCommand(() => GuardBg(() => _mount.Protocol.FocuserSetHomeHere()), () => MountActionsEnabled);
            GoHomeCommand        = new RelayCommand(() => GuardBg(() => { _mount.Protocol.SetFocuserRatePreset(_gotoRatePreset); _mount.Protocol.FocuserGoHome(); }), () => MountActionsEnabled);
            ZeroCommand          = new RelayCommand(() => GuardBg(() => _mount.Protocol.FocuserZero()),       () => MountActionsEnabled);
            ApplyBacklashCommand = new RelayCommand(DoApplyBacklash, () => MountActionsEnabled);
            ApplyTcfCommand      = new RelayCommand(DoApplyTcf,      () => MountActionsEnabled && TempCompAvailable);
            OpenAdvancedCommand  = new RelayCommand(OpenAdvanced);
        }

        private void OpenAdvanced()
        {
            var dlg = new Views.FocuserAdvancedWindow(this)
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

        // Fan-out from MainViewModel.OnConnected. Reads NV-backed config once.
        public void OnConnected()
        {
            // The MountStateCache probe already filled FocuserAvailable / Count.
            var st = _mount.State;
            if (st == null) return;
            IsAvailable = st.FocuserAvailable;
            if (!IsAvailable) { Count = 0; return; }
            Count = Math.Max(1, st.FocuserCount);

            // Honour user's preferred default if it's in range; otherwise stay
            // on whatever the firmware reports as currently active.
            int preferred = DriverSettings.FocuserDefaultIndex;
            int active = (preferred >= 1 && preferred <= Count) ? preferred : st.FocuserActiveIndex;
            if (active < 1 || active > Count) active = 1;

            _suppressActiveIndexEvent = true;
            try { ActiveIndex = active; }
            finally { _suppressActiveIndexEvent = false; }

            try { _mount.Protocol.SetActiveFocuser(active); } catch { }
            RefreshNvBackedState();
        }

        public void OnDisconnected()
        {
            IsAvailable = false;
            Count = 0;
            Position = 0;
            PositionMm = "—";
            TemperatureText = "—";
            StatusKind = StatusKind.Neutral;
            StatusText = "Idle";
            StatusPulse = false;
        }

        // Poll-driven; called from MainViewModel's 250 ms pump.
        internal void OnPollSnapshot(MountStateCache st)
        {
            if (!st.FocuserAvailable) { if (IsAvailable) OnDisconnected(); return; }

            // Late detection: cache's lazy re-probe just succeeded after our
            // OnConnected gave up. Run the same connect-time setup so the
            // section's NV-backed fields (min/max/backlash/TCF/coeffs) populate.
            if (!IsAvailable)
            {
                IsAvailable = true;
                Count = Math.Max(1, st.FocuserCount);
                int preferred = DriverSettings.FocuserDefaultIndex;
                int active = (preferred >= 1 && preferred <= Count) ? preferred : st.FocuserActiveIndex;
                if (active < 1 || active > Count) active = 1;
                _suppressActiveIndexEvent = true;
                try { ActiveIndex = active; }
                finally { _suppressActiveIndexEvent = false; }
                try { _mount.Protocol.SetActiveFocuser(active); } catch { }
                RefreshNvBackedState();
            }

            Position = st.FocuserPosition;
            Moving = st.FocuserMoving;
            PositionMm = StepSizeMicrons > 0
                ? (Position * StepSizeMicrons / 1000.0).ToString("0.000", CultureInfo.InvariantCulture) + " mm"
                : "—";

            TemperatureText = double.IsNaN(st.FocuserTempC) || double.IsInfinity(st.FocuserTempC) || Math.Abs(st.FocuserTempC) > 1000.0
                ? "—"
                : st.FocuserTempC.ToString("0.0", CultureInfo.InvariantCulture) + " °C";

            if (Moving)        { StatusKind = StatusKind.Info;    StatusText = "Moving"; StatusPulse = true; }
            else if (TempCompEnabled) { StatusKind = StatusKind.Ok; StatusText = "TCF on"; StatusPulse = false; }
            else               { StatusKind = StatusKind.Neutral; StatusText = "Idle";   StatusPulse = false; }
        }

        // Re-read NV-backed config (called on connect and on focuser-switch).
        // Each read independently guarded so a single failure doesn't blank the
        // whole section.
        private void RefreshNvBackedState()
        {
            if (_main.State != ConnState.Connected) return;
            // Dispatch the multi-roundtrip read pass to the threadpool — this
            // path runs from OnConnected and from the ActiveIndex setter on
            // the UI thread.
            RunBg(RefreshNvBackedStateBg);
        }

        private void RefreshNvBackedStateBg()
        {
            if (_main.State != ConnState.Connected) return;
            int min = 0, max = 0, backlash = 0, tcfDeadband = 0, target = 0;
            double stepSize = 0, tcfCoeff = 0, t = double.NaN;
            bool tcfOn = false;
            try { min        = _mount.Protocol.GetFocuserMinSteps(); }       catch { }
            try { max        = _mount.Protocol.GetFocuserMaxSteps(); }       catch { }
            try { stepSize   = _mount.Protocol.GetFocuserMicronsPerStep(); } catch { }
            try { backlash   = _mount.Protocol.GetFocuserBacklashSteps(); }  catch { }
            try { t          = _mount.Protocol.GetFocuserTemperatureC(); }   catch { }
            try { tcfOn      = _mount.Protocol.GetFocuserTcfEnabled(); }     catch { }
            try { tcfCoeff   = _mount.Protocol.GetFocuserTcfCoeffUmPerC(); } catch { }
            try { tcfDeadband= _mount.Protocol.GetFocuserTcfDeadbandSteps();} catch { }
            try { target     = _mount.Protocol.GetFocuserPositionSteps(); }  catch { }
            bool tcfAvail = !double.IsNaN(t) && !double.IsInfinity(t) && Math.Abs(t) < 1000.0;

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                MinSteps        = min;
                MaxSteps        = max;
                StepSizeMicrons = stepSize;
                Backlash        = backlash;
                TempCompAvailable = tcfAvail;
                _suppressTcfEvent = true;
                try { TempCompEnabled = tcfOn; }
                finally { _suppressTcfEvent = false; }
                TcfCoeff        = tcfCoeff;
                TcfDeadband     = tcfDeadband;
                TargetPosition  = target;
            }));
        }

        // ---------- Command bodies ----------
        private void DoGoto()
        {
            if (!MountActionsEnabled) return;
            int target = TargetPosition;
            if (target < MinSteps || target > MaxSteps)
            {
                Views.CopyableMessage.Show("Focuser",
                    "Target " + target + " is outside the firmware limits [" + MinSteps + ".." + MaxSteps + "].");
                return;
            }
            int rate = _gotoRatePreset;
            RunBg(() =>
            {
                // Reapply goto-rate preset so a prior In/Out click (which set
                // the move-rate register, 1..4) doesn't leave the firmware on
                // the wrong rate band when we issue :Fs#.
                try { _mount.Protocol.SetFocuserRatePreset(rate); } catch { }
                if (!_mount.Protocol.SetFocuserPositionSteps(target))
                {
                    string err = ""; try { err = _mount.Protocol.GetLastError(); } catch { }
                    ShowOnUi("Goto rejected by mount." +
                        (string.IsNullOrEmpty(err) ? "" : "\r\nMount error: " + err));
                }
            });
        }

        private void DoHalt() => GuardBg(() => _mount.Protocol.FocuserHalt());

        // Step-based manual move via :Fr[±N]#. OnStepX convention: positive
        // delta increases position (= "out"), negative delta = "in" toward
        // the objective. :Fr# runs on the goto-rate register so reapply the
        // user's selected goto rate before issuing the relative move.
        private void DoMoveIn()
        {
            int step = _stepSize, rate = _gotoRatePreset;
            GuardBg(() => { _mount.Protocol.SetFocuserRatePreset(rate); _mount.Protocol.SetFocuserPositionRelativeSteps(-step); });
        }
        private void DoMoveOut()
        {
            int step = _stepSize, rate = _gotoRatePreset;
            GuardBg(() => { _mount.Protocol.SetFocuserRatePreset(rate); _mount.Protocol.SetFocuserPositionRelativeSteps(+step); });
        }

        private void DoApplyBacklash()
        {
            if (!MountActionsEnabled) return;
            int v = Backlash;
            RunBg(() =>
            {
                if (!_mount.Protocol.SetFocuserBacklashSteps(v))
                {
                    string err = ""; try { err = _mount.Protocol.GetLastError(); } catch { }
                    ShowOnUi("Backlash apply rejected." +
                        (string.IsNullOrEmpty(err) ? "" : "\r\nMount error: " + err));
                }
            });
        }

        private void DoApplyTcf()
        {
            if (!MountActionsEnabled || !TempCompAvailable) return;
            double coeff = TcfCoeff; int dead = TcfDeadband;
            RunBg(() =>
            {
                _mount.Protocol.SetFocuserTcfCoeffUmPerC(coeff);
                _mount.Protocol.SetFocuserTcfDeadbandSteps(dead);
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
                catch (Exception ex) { DebugLogger.LogException("FOCUSER", ex); }
            });
        }

        private static void ShowOnUi(string msg)
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    Views.CopyableMessage.Show("Focuser", msg)));
            }
            catch { }
        }
    }

    // Display label + raw OnStepX preset number (1..9) for the rate combos.
    public sealed class RateOption
    {
        public int Value { get; }
        public string Label { get; }
        public RateOption(int value, string label) { Value = value; Label = label; }
        public override string ToString() => Label;
    }
}
