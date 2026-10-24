using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ASCOM.OnStepX.Diagnostics;
using ASCOM.OnStepX.Hardware;

namespace ASCOM.OnStepX.ViewModels
{
    // Live home-switch readout, for finding where each switch changes state
    // without taking the mount apart. Polls :hS#, which stock OnStepX does
    // not have — it comes from the home-sense diagnostics firmware patch.
    //
    // Homing picks its direction from the switch alone (Axis::autoSlewHome:
    // ON drives forward, OFF drives reverse) after resetting the position to
    // home, so a dead switch or a wrong HIGH/LOW setting sends :hC# the wrong
    // way until it times out. Jogging an axis slowly across where home should
    // be shows the angle the switch flips at, or that it never does.
    public sealed class HomeSwitchesViewModel : ViewModelBase
    {
        private const int PollMs = 250;
        private const int MaxTransitions = 50;
        private const int MaxConsecutiveFailures = 4;

        private readonly MainViewModel _main;
        private readonly MountSession _mount = MountSession.Instance;
        private CancellationTokenSource _cts;

        public HomeSwitchAxis[] Axes { get; } =
        {
            new HomeSwitchAxis("Axis 1 (RA)"),
            new HomeSwitchAxis("Axis 2 (Dec)"),
        };
        public ObservableCollection<HomeSwitchTransition> Transitions { get; } = new ObservableCollection<HomeSwitchTransition>();

        public bool MountActionsEnabled => _main.State == ConnState.Connected;

        private bool _isMonitoring;
        public bool IsMonitoring
        {
            get => _isMonitoring;
            set
            {
                if (value && !MountActionsEnabled) value = false;
                if (!Set(ref _isMonitoring, value)) return;
                if (value) Start(); else Stop();
            }
        }

        private string _statusText = "Not monitoring.";
        public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

        // What the controller found on its I2C bus at boot. "Ok" / "Fail" /
        // "Unknown" drive the LED colour in XAML.
        private string _expanderKey = "Unknown";
        public string ExpanderKey { get => _expanderKey; private set => Set(ref _expanderKey, value); }
        private string _expanderText = "—";
        public string ExpanderText { get => _expanderText; private set => Set(ref _expanderText, value); }

        private string _nvKey = "Unknown";
        public string NvKey { get => _nvKey; private set => Set(ref _nvKey, value); }
        private string _nvText = "—";
        public string NvText { get => _nvText; private set => Set(ref _nvText, value); }

        public ICommand ClearCommand { get; }

        public HomeSwitchesViewModel(MainViewModel main)
        {
            _main = main;
            ClearCommand = new RelayCommand(() => Transitions.Clear());
        }

        internal void OnConnStateChanged()
        {
            OnPropertyChanged(nameof(MountActionsEnabled));
            if (!MountActionsEnabled) IsMonitoring = false;
        }

        internal void Detach() => Stop();

        private void Start()
        {
            Stop();
            var cts = new CancellationTokenSource();
            _cts = cts;
            foreach (var a in Axes) a.Reset();
            ExpanderKey = NvKey = "Unknown";
            ExpanderText = NvText = "—";
            StatusText = "Monitoring…";
            DebugLogger.Log("HOMESENSE", "monitor started");
            Task.Run(() => PollLoop(cts.Token));
        }

        private void Stop()
        {
            var cts = _cts;
            _cts = null;
            if (cts == null) return;
            try { cts.Cancel(); } catch { }
            DebugLogger.Log("HOMESENSE", "monitor stopped");
        }

        private void PollLoop(CancellationToken ct)
        {
            var prevState = new[] { int.MinValue, int.MinValue };
            var prevAngle = new[] { double.NaN, double.NaN };
            int failures = 0;

            while (!ct.IsCancellationRequested)
            {
                HomeSenseReading r = null;
                var angle = new[] { double.NaN, double.NaN };
                try
                {
                    r = HomeSenseReading.Parse(_mount.Protocol.GetHomeSenseRaw());
                    try { angle[0] = _mount.Protocol.GetAxis1Degrees(); } catch { }
                    try { angle[1] = _mount.Protocol.GetAxis2Degrees(); } catch { }
                }
                catch (Exception ex) { DebugLogger.LogException("HOMESENSE", ex); }

                if (r == null)
                {
                    // One bad read is a wire hiccup; several in a row is a
                    // firmware that doesn't know :hS#.
                    if (++failures >= MaxConsecutiveFailures)
                    {
                        DebugLogger.Log("HOMESENSE", "no valid :hS# reply after " + failures + " tries; stopping");
                        Ui(() =>
                        {
                            IsMonitoring = false;
                            StatusText = "The mount doesn't answer :hS#. This firmware doesn't have the " +
                                         "home-switch diagnostics command; flash the patched firmware first.";
                        });
                        return;
                    }
                }
                else
                {
                    failures = 0;
                    var changes = new List<HomeSwitchTransition>();
                    for (int i = 0; i < 2; i++)
                    {
                        int s = r.State[i];
                        if (prevState[i] != int.MinValue && s != prevState[i])
                        {
                            var t = new HomeSwitchTransition(Axes[i].Name, prevState[i], s, prevAngle[i], angle[i]);
                            changes.Add(t);
                            DebugLogger.Log("HOMESENSE", Axes[i].Name + " " + t.Change + " " + t.Where);
                        }
                        prevState[i] = s;
                        prevAngle[i] = angle[i];
                    }

                    string status;
                    if (r.GpioMissing)
                        // The expander driver answers every read with 0 when the
                        // chip didn't respond at boot, so a LOW-triggered switch
                        // reads ON for good and homing drives one way until it
                        // times out.
                        status = "The I/O expander that carries the home switches was NOT found when the " +
                                 "controller booted. Every switch read returns LOW, so the switches can't " +
                                 "change and Go Home drives one way until it times out. Check the expander's " +
                                 "I2C wiring and power, then power-cycle the controller.";
                    else if (r.Trigger[0] == -1 && r.Trigger[1] == -1)
                        status = "This firmware has no home switches configured (AXIS1/AXIS2_SENSE_HOME OFF), " +
                                 "so \"Go Home\" runs as a plain goto and never reads a switch.";
                    else
                        status = "Monitoring. Jog an axis slowly across home and watch for transitions.";

                    Ui(() =>
                    {
                        ExpanderKey  = r.GpioMissing ? "Fail" : "Ok";
                        ExpanderText = r.GpioMissing ? "NOT FOUND at boot" : "found at boot";
                        NvKey        = r.NvError ? "Fail" : "Ok";
                        NvText       = r.NvError ? "unusable at boot" : "OK at boot";
                        for (int i = 0; i < 2; i++) Axes[i].Update(r.State[i], r.Trigger[i], r.Reverse[i], angle[i]);
                        foreach (var c in changes) Transitions.Insert(0, c);
                        while (Transitions.Count > MaxTransitions) Transitions.RemoveAt(Transitions.Count - 1);
                        if (IsMonitoring) StatusText = status;
                    });
                }

                try { Task.Delay(PollMs, ct).Wait(ct); } catch { }
            }
        }

        private static void Ui(Action a)
        {
            try { Application.Current?.Dispatcher.BeginInvoke(a); } catch { }
        }
    }

    // One :hS# reply, "s1,t1,r1,s2,t2,r2,g,n". Per axis: state -1 no switch /
    // 0 off / 1 on (as homing sees it, after trigger and reverse), the
    // configured trigger (HIGH 1, LOW 0, OFF -1, otherwise an analog
    // threshold), and whether the sense direction is effectively reversed.
    // Then two boot flags: g = the GPIO expander carrying the switches wasn't
    // found, n = NV was unusable. Both sit on I2C, so one bus fault can take
    // out the switches and the coordinate memory together.
    internal sealed class HomeSenseReading
    {
        public readonly int[] State = new int[2];
        public readonly long[] Trigger = new long[2];
        public readonly bool[] Reverse = new bool[2];
        public bool GpioMissing;
        public bool NvError;

        public static HomeSenseReading Parse(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            var parts = raw.TrimEnd('#').Split(',');
            if (parts.Length != 8) return null;
            var r = new HomeSenseReading();
            for (int i = 0; i < 2; i++)
            {
                if (!int.TryParse(parts[i * 3], NumberStyles.Integer, CultureInfo.InvariantCulture, out r.State[i])) return null;
                if (!long.TryParse(parts[i * 3 + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out r.Trigger[i])) return null;
                if (!int.TryParse(parts[i * 3 + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rev)) return null;
                r.Reverse[i] = rev != 0;
            }
            if (!int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int g)) return null;
            if (!int.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return null;
            r.GpioMissing = g != 0;
            r.NvError = n != 0;
            return r;
        }
    }

    public sealed class HomeSwitchAxis : ViewModelBase
    {
        public string Name { get; }
        public HomeSwitchAxis(string name) { Name = name; Reset(); }

        // "On" / "Off" / "None" / "Unknown" — drives the LED colour in XAML.
        private string _stateKey;
        public string StateKey { get => _stateKey; private set => Set(ref _stateKey, value); }

        private string _stateText;
        public string StateText { get => _stateText; private set => Set(ref _stateText, value); }

        private string _pinText;
        public string PinText { get => _pinText; private set => Set(ref _pinText, value); }

        private string _triggerText;
        public string TriggerText { get => _triggerText; private set => Set(ref _triggerText, value); }

        private string _reverseText;
        public string ReverseText { get => _reverseText; private set => Set(ref _reverseText, value); }

        private string _angleText;
        public string AngleText { get => _angleText; private set => Set(ref _angleText, value); }

        public void Reset()
        {
            StateKey = "Unknown";
            StateText = "—";
            PinText = "—";
            TriggerText = "—";
            ReverseText = "—";
            AngleText = "—";
        }

        public void Update(int state, long trigger, bool reverse, double angleDeg)
        {
            AngleText = double.IsNaN(angleDeg) ? "—" : angleDeg.ToString("F2", CultureInfo.InvariantCulture) + "°";
            if (state < 0 || trigger == -1)
            {
                StateKey = "None";
                StateText = "no switch";
                PinText = "—";
                TriggerText = "OFF";
                ReverseText = "—";
                return;
            }

            bool on = state != 0;
            StateKey = on ? "On" : "Off";
            StateText = on ? "ON" : "OFF";
            ReverseText = reverse ? "reversed" : "normal";

            // Trigger packing from SenseInput: bit 0 is the active level,
            // bits 1-10 an analog threshold (non-zero means analog input).
            bool activeHigh = (trigger & 1) != 0;
            bool analog = ((trigger >> 1) & 0x3FF) != 0;
            TriggerText = analog ? "analog" : (activeHigh ? "HIGH" : "LOW");

            // isOn() is (pin XOR reverse) == active, so the pin level falls out.
            if (analog) { PinText = "analog"; return; }
            bool level = on ? activeHigh : !activeHigh;
            if (reverse) level = !level;
            PinText = level ? "HIGH" : "LOW";
        }
    }

    public sealed class HomeSwitchTransition
    {
        public string Time { get; }
        public string Axis { get; }
        public string Change { get; }
        public string Where { get; }

        public HomeSwitchTransition(string axis, int from, int to, double fromDeg, double toDeg)
        {
            Time = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            Axis = axis;
            Change = Label(from) + " → " + Label(to);
            // The switch flipped somewhere between the previous poll and this
            // one, so bracket it with the angles read on each.
            if (double.IsNaN(toDeg)) Where = "";
            else if (double.IsNaN(fromDeg) || Math.Abs(fromDeg - toDeg) < 0.005)
                Where = "at " + toDeg.ToString("F2", CultureInfo.InvariantCulture) + "°";
            else
                Where = "between " + fromDeg.ToString("F2", CultureInfo.InvariantCulture) + "° and " +
                        toDeg.ToString("F2", CultureInfo.InvariantCulture) + "°";
        }

        private static string Label(int s) => s < 0 ? "none" : s == 0 ? "OFF" : "ON";
    }
}
