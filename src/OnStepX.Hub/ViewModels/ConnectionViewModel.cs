using System;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Diagnostics;
using ASCOM.OnStepX.Hardware;
using ASCOM.OnStepX.Hardware.Transport;

namespace ASCOM.OnStepX.ViewModels
{
    // Connection card. Mirrors HubForm.BuildConnectionGroup + DoConnect/DoDisconnect/
    // DoAutoDetect. Owns the two-stage connect state machine — UI pivots on State
    // (Disconnected / Connecting / Connected) so transport probes don't enable
    // mount-action controls until the firmware is responsive.
    public sealed class ConnectionViewModel : ViewModelBase
    {
        private readonly MainViewModel _main;
        private readonly MountSession _mount = MountSession.Instance;
        private CancellationTokenSource _connectCts;

        // Mount slew base/us-per-step probed on connect; surfaced to TrackingViewModel
        // for the slew-rate UI tightening.
        public double MountBaseSlewDegPerSec { get; private set; }
        public double MountUsPerStepBase     { get; private set; }

        public ObservableCollection<string> Transports { get; } = new ObservableCollection<string> { "Serial", "TCP" };
        public ObservableCollection<string> ComPorts { get; } = new ObservableCollection<string>();

        private string _transportKind;
        public string TransportKind
        {
            get => _transportKind;
            set { if (Set(ref _transportKind, value)) { OnPropertyChanged(nameof(IsSerial)); OnPropertyChanged(nameof(IsTcp)); } }
        }
        public bool IsSerial => string.Equals(_transportKind, "Serial", StringComparison.OrdinalIgnoreCase);
        public bool IsTcp    => string.Equals(_transportKind, "TCP",    StringComparison.OrdinalIgnoreCase);

        private string _serialPort;  public string SerialPort { get => _serialPort; set => Set(ref _serialPort, value); }
        private int _serialBaud;     public int SerialBaud { get => _serialBaud; set => Set(ref _serialBaud, value); }
        private string _tcpHost;     public string TcpHost { get => _tcpHost; set => Set(ref _tcpHost, value); }
        private int _tcpPort;        public int TcpPort { get => _tcpPort; set => Set(ref _tcpPort, value); }

        private bool _autoConnect;
        public bool AutoConnect
        {
            get => _autoConnect;
            set { if (Set(ref _autoConnect, value)) { try { DriverSettings.AutoConnect = value; } catch { } } }
        }

        public ICommand AutoDetectCommand { get; }
        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }

        // UI gating. Mirrors HubForm.ApplyConnState's per-state control enable/disable.
        public bool ConnectButtonEnabled    => _main.State == ConnState.Disconnected;
        public bool DisconnectButtonEnabled => _main.State == ConnState.Connected;
        public bool TransportFieldsEnabled  => _main.State == ConnState.Disconnected;
        public bool AutoDetectEnabled       => _main.State == ConnState.Disconnected && !_isAutoDetecting;
        private bool _isAutoDetecting;

        public string AutoDetectButtonText => _isAutoDetecting ? "Scanning…" : "Auto-Detect";

        public StatusKind StatusKind => _main.State switch
        {
            ConnState.Connected    => StatusKind.Ok,
            ConnState.Connecting   => StatusKind.Warn,
            _                      => StatusKind.Err
        };
        public string StatusText => _main.State switch
        {
            ConnState.Connected    => "Connected",
            ConnState.Connecting   => "Connecting...",
            _                      => "Disconnected"
        };
        public bool StatusPulse => _main.State == ConnState.Connected;

        public ConnectionViewModel(MainViewModel main)
        {
            _main = main;
            AutoDetectCommand = new RelayCommand(DoAutoDetect, () => AutoDetectEnabled);
            ConnectCommand    = new RelayCommand(DoConnect,    () => ConnectButtonEnabled);
            DisconnectCommand = new RelayCommand(DoDisconnect, () => DisconnectButtonEnabled);

            RefreshSerialPorts();
            LoadFromSettings();
        }

        // Re-raise enable/status props when MainViewModel.State flips.
        internal void OnConnStateChanged()
        {
            OnPropertyChanged(nameof(ConnectButtonEnabled));
            OnPropertyChanged(nameof(DisconnectButtonEnabled));
            OnPropertyChanged(nameof(TransportFieldsEnabled));
            OnPropertyChanged(nameof(AutoDetectEnabled));
            OnPropertyChanged(nameof(StatusKind));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusPulse));
            CommandManager.InvalidateRequerySuggested();
        }

        public void RefreshSerialPorts()
        {
            ComPorts.Clear();
            foreach (var p in System.IO.Ports.SerialPort.GetPortNames()) ComPorts.Add(p);
        }

        private void LoadFromSettings()
        {
            TransportKind = DriverSettings.TransportKind ?? "Serial";
            SerialPort    = DriverSettings.SerialPort ?? "COM3";
            SerialBaud    = DriverSettings.SerialBaud;
            TcpHost       = DriverSettings.TcpHost ?? "192.168.0.1";
            TcpPort       = DriverSettings.TcpPort;
            _autoConnect  = DriverSettings.AutoConnect;
            OnPropertyChanged(nameof(AutoConnect));
        }

        private void SaveOnConnect()
        {
            DriverSettings.TransportKind = TransportKind;
            DriverSettings.SerialPort    = SerialPort;
            DriverSettings.SerialBaud    = SerialBaud;
            DriverSettings.TcpHost       = TcpHost;
            DriverSettings.TcpPort       = TcpPort;
        }

        public bool HasUsablePortSetting()
        {
            string kind = (TransportKind ?? "").Trim();
            if (kind.Equals("TCP", StringComparison.OrdinalIgnoreCase))
                return !string.IsNullOrWhiteSpace(TcpHost) && TcpPort > 0;
            string port = (SerialPort ?? "").Trim();
            if (string.IsNullOrEmpty(port)) return false;
            foreach (var p in System.IO.Ports.SerialPort.GetPortNames())
                if (string.Equals(p, port, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Synchronous-on-UI auto-detect. PortAutoDetect itself does the wire probe;
        // 1-3 s blocking is acceptable since the user explicitly clicked Auto-Detect
        // and the button text shows "Scanning…".
        private void DoAutoDetect()
        {
            _isAutoDetecting = true;
            OnPropertyChanged(nameof(AutoDetectEnabled));
            OnPropertyChanged(nameof(AutoDetectButtonText));
            try
            {
                var port = PortAutoDetect.FindOnStepPort();
                if (port != null)
                {
                    TransportKind = "Serial";
                    SerialPort = port;
                    MessageBox.Show("Found OnStepX on " + port, "Auto-Detect", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("No OnStepX mount detected on serial ports.", "Auto-Detect", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            finally
            {
                _isAutoDetecting = false;
                OnPropertyChanged(nameof(AutoDetectEnabled));
                OnPropertyChanged(nameof(AutoDetectButtonText));
            }
        }

        public void DoConnect()
        {
            // Snapshot UI state on UI thread so the worker doesn't race a user edit.
            string kind = TransportKind;
            string host = TcpHost;
            int tcpPort = TcpPort;
            string port = SerialPort;
            int baud = SerialBaud;

            _main.SetState(ConnState.Connecting);

            var cts = new CancellationTokenSource();
            try { _connectCts?.Cancel(); _connectCts?.Dispose(); } catch { }
            _connectCts = cts;

            Task.Run(() =>
            {
                Exception err = null;
                bool canceled = false;
                double mountBaseSlew = 0.0;
                double mountUsPerStepBase = 0.0;
                try
                {
                    ITransport t = string.Equals(kind, "TCP", StringComparison.OrdinalIgnoreCase)
                        ? (ITransport)new TcpTransport(host, tcpPort)
                        : new SerialTransport(port, baud);
                    _mount.Configure(t);
                    _mount.Open(cts.Token);
                    try { mountBaseSlew = _mount.Protocol.GetBaseSlewRateDegPerSec(); } catch { }
                    try { mountUsPerStepBase = _mount.Protocol.GetUsPerStepBase(); } catch { }
                }
                catch (OperationCanceledException) { canceled = true; }
                catch (Exception ex) { err = ex; }

                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (canceled)
                    {
                        _main.SetState(ConnState.Disconnected);
                        return;
                    }
                    if (err != null)
                    {
                        _main.SetState(ConnState.Disconnected);
                        Views.CopyableMessage.Show("Connect failed", err.ToString());
                        return;
                    }

                    MountBaseSlewDegPerSec = mountBaseSlew;
                    MountUsPerStepBase = mountUsPerStepBase;

                    SaveOnConnect();
                    _main.OnConnected();
                }));

                try
                {
                    if (ReferenceEquals(_connectCts, cts)) _connectCts = null;
                    cts.Dispose();
                }
                catch { }
            });
        }

        public void DoDisconnect()
        {
            try { _connectCts?.Cancel(); } catch { }
            try { _mount.Close(); } catch { }
            _main.SetState(ConnState.Disconnected);
        }
    }
}
