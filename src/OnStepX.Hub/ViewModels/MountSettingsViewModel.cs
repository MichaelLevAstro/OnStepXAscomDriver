using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using ASCOM.OnStepX.Diagnostics;
using ASCOM.OnStepX.Hardware;
using ASCOM.OnStepX.Hardware.Transport;

namespace ASCOM.OnStepX.ViewModels
{
    // Mount Settings card. Exposes the OnStepX mount-type override (:SXEM,n#)
    // so the hub can toggle a multi-mode mount between German Equatorial,
    // Equatorial Fork, and Alt-Az without re-flashing firmware. Mount type
    // is persisted to NV by the firmware, so the change requires a reboot
    // — Apply sends :SXEM, then :ERESET#, then disconnects the hub.
    public sealed class MountSettingsViewModel : ViewModelBase
    {
        private readonly MainViewModel _main;
        private readonly MountSession _mount = MountSession.Instance;

        public sealed class MountModeOption
        {
            public string Display { get; set; }
            public int    Code    { get; set; }   // 1=GEM, 2=Eq Fork, 3=Alt-Az
            public override string ToString() => Display;
        }

        public List<MountModeOption> Modes { get; } = new List<MountModeOption>
        {
            new MountModeOption { Display = "Equatorial — German (GEM)", Code = 1 },
            new MountModeOption { Display = "Equatorial — Fork",          Code = 2 },
            new MountModeOption { Display = "Alt-Az",                     Code = 3 },
        };

        private MountModeOption _selectedMode;
        public MountModeOption SelectedMode
        {
            get => _selectedMode;
            set { if (Set(ref _selectedMode, value)) CommandManager.InvalidateRequerySuggested(); }
        }

        // Last value read from the mount (-1 means unknown / firmware doesn't
        // expose :GXEM#). Drives the "Current mode" label.
        private int _currentMode = -1;
        public int CurrentMode
        {
            get => _currentMode;
            private set
            {
                if (!Set(ref _currentMode, value)) return;
                OnPropertyChanged(nameof(CurrentModeText));
            }
        }
        public string CurrentModeText
        {
            get
            {
                switch (_currentMode)
                {
                    case 1: return "German Equatorial";
                    case 2: return "Equatorial Fork";
                    case 3: return "Alt-Az";
                    case 0: return "Uninitialized";
                    default: return "Unknown";
                }
            }
        }

        public bool MountActionsEnabled => _main.State == ConnState.Connected;
        public bool ApplyEnabled =>
            MountActionsEnabled && SelectedMode != null && SelectedMode.Code != _currentMode;

        public ICommand ApplyCommand { get; }
        public ICommand RefreshCommand { get; }

        public MountSettingsViewModel(MainViewModel main)
        {
            _main = main;
            ApplyCommand   = new RelayCommand(DoApply,   () => ApplyEnabled);
            RefreshCommand = new RelayCommand(DoRefresh, () => MountActionsEnabled);
            _selectedMode = Modes[0];
        }

        internal void OnConnStateChanged()
        {
            OnPropertyChanged(nameof(MountActionsEnabled));
            OnPropertyChanged(nameof(ApplyEnabled));
            CommandManager.InvalidateRequerySuggested();
            if (_main.State != ConnState.Connected)
            {
                CurrentMode = -1;
            }
        }

        // Pull the current mount type after Stage-2 connect. Best-effort: a
        // firmware that doesn't recognize :GXEM# leaves CurrentMode at -1 and
        // the section still works for first-time configuration via Apply.
        public void OnConnected()
        {
            DoRefresh();
        }

        private void DoRefresh()
        {
            if (_main.State != ConnState.Connected) return;
            try
            {
                int mt = _mount.Protocol.GetMountType();
                CurrentMode = mt;
                var match = Modes.Find(m => m.Code == mt);
                if (match != null) SelectedMode = match;
                OnPropertyChanged(nameof(ApplyEnabled));
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                TransportLogger.Note("Mount type read failed: " + ex.Message);
            }
        }

        private void DoApply()
        {
            if (_main.State != ConnState.Connected) return;
            var mode = SelectedMode;
            if (mode == null) return;

            string warning =
                "Switch mount mode to \"" + mode.Display + "\"?\r\n\r\n" +
                "OnStepX persists the mount type to non-volatile memory and " +
                "applies it only after a reboot.\r\n\r\n" +
                "The hub will:\r\n" +
                "  1. Send the new mount type to the mount (:SXEM," + mode.Code + "#)\r\n" +
                "  2. Reboot the mount (:ERESET#)\r\n" +
                "  3. Disconnect this hub session\r\n\r\n" +
                "Wait ~10 seconds after the reboot, then reconnect.\r\n\r\n" +
                "Continue?";
            var r = MessageBox.Show(warning, "Switch mount mode",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (r != MessageBoxResult.Yes) return;

            bool accepted = false;
            string err = "";
            try { accepted = _mount.Protocol.SetMountType(mode.Code); }
            catch (Exception ex) { err = ex.Message; }
            if (!accepted)
            {
                try { err = string.IsNullOrEmpty(err) ? _mount.Protocol.GetLastError() : err; } catch { }
                err = (err ?? "").TrimEnd('#').Trim();
                string hint = mode.Code == 3
                    ? "ALTAZM requires AXIS2_TANGENT_ARM = OFF in the firmware Config.h."
                    : "GEM and FORK require AXIS1_WRAP = OFF in the firmware Config.h.";
                MessageBox.Show(
                    "Mount rejected the mode change." +
                    (string.IsNullOrEmpty(err) ? "" : "\r\nMount error: " + err) +
                    "\r\n\r\n" + hint,
                    "Switch mount mode", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Thread.Sleep(250);
                _mount.Protocol.RebootMount();
            }
            catch (Exception ex)
            {
                Views.CopyableMessage.Show("Switch mount mode", "Reboot send failed:\r\n\r\n" + ex.ToString());
            }
            try { _main.Connection.DoDisconnect(); } catch { }
            MessageBox.Show(
                "Mount mode change sent. Mount is rebooting.\r\n" +
                "Wait ~10 seconds, then reconnect.",
                "Switch mount mode", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
