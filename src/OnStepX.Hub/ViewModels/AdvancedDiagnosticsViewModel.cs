using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Diagnostics;

namespace ASCOM.OnStepX.ViewModels
{
    // The collapsed-by-default "Advanced Settings" card on the main window.
    // (Distinct from AdvancedSettingsViewModel which backs the modal pier/flip
    // dialog.) Mirrors HubForm.BuildAdvancedGroup. Also surfaces the Polar
    // Alignment Wedge mode toggle so users don't have to dig into the modal.
    public sealed class AdvancedDiagnosticsViewModel : ViewModelBase
    {
        private bool _notificationsEnabled;
        public bool NotificationsEnabled
        {
            get => _notificationsEnabled;
            set { if (Set(ref _notificationsEnabled, value)) { try { DriverSettings.NotificationsEnabled = value; } catch { } } }
        }

        private bool _verboseLog;
        public bool VerboseLog
        {
            get => _verboseLog;
            set { if (Set(ref _verboseLog, value)) { try { DriverSettings.VerboseFileLog = value; } catch { } } }
        }

        // Polar Alignment Wedge mode. Persisted on toggle. Reconnect required
        // for the effect to land (MountStateCache resolves it during Start()).
        private bool _polarAlignmentMode;
        public bool PolarAlignmentMode
        {
            get => _polarAlignmentMode;
            set { if (Set(ref _polarAlignmentMode, value)) { try { DriverSettings.PolarAlignmentMode = value; } catch { } } }
        }

        // Local serial port the hub opens for the NINA TPPA UPAS bridge.
        // Persisted; bridge reconciles on save (handled in setter via _onBridgeChange).
        private string _tppaBridgePort = "";
        public string TppaBridgePort
        {
            get => _tppaBridgePort;
            set
            {
                string v = value ?? "";
                if (!Set(ref _tppaBridgePort, v)) return;
                try { DriverSettings.TppaBridgePort = v; } catch { }
                try { _onBridgeChange?.Invoke(); } catch { }
            }
        }
        private Action _onBridgeChange;
        internal void SetBridgeChangeHandler(Action handler) { _onBridgeChange = handler; }

        public string LogPath => DebugLogger.LogDirectory;

        public ICommand OpenLogFolderCommand { get; }

        public AdvancedDiagnosticsViewModel()
        {
            _notificationsEnabled = DriverSettings.NotificationsEnabled;
            _verboseLog = DriverSettings.VerboseFileLog;
            _polarAlignmentMode = DriverSettings.PolarAlignmentMode;
            _tppaBridgePort = DriverSettings.TppaBridgePort ?? "";
            OpenLogFolderCommand = new RelayCommand(OpenLogFolder);
        }

        private void OpenLogFolder()
        {
            try
            {
                Directory.CreateDirectory(DebugLogger.LogDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = DebugLogger.LogDirectory,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open log folder:\n" + ex.Message,
                    "OnStepX", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
