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
    // dialog.) Mirrors HubForm.BuildAdvancedGroup.
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

        public string LogPath => DebugLogger.LogDirectory;

        public ICommand OpenLogFolderCommand { get; }

        public AdvancedDiagnosticsViewModel()
        {
            _notificationsEnabled = DriverSettings.NotificationsEnabled;
            _verboseLog = DriverSettings.VerboseFileLog;
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
