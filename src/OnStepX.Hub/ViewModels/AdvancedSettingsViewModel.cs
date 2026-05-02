using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Hardware;

namespace ASCOM.OnStepX.ViewModels
{
    // Mirrors AdvancedSettingsForm. Apply pushes preferred pier + pause-at-home
    // to the mount and persists to DriverSettings; OK applies then closes.
    public sealed class AdvancedSettingsViewModel : ViewModelBase
    {
        private readonly MainViewModel _main;
        private readonly MountSession _mount = MountSession.Instance;

        public ObservableCollection<string> PierOptions { get; } = new ObservableCollection<string>
        {
            "Best (stay on current side)",
            "East",
            "West",
            "Auto",
        };

        private int _preferredPierIndex;
        public int PreferredPierIndex { get => _preferredPierIndex; set => Set(ref _preferredPierIndex, Math.Max(0, Math.Min(3, value))); }

        private bool _pauseAtHome;
        public bool PauseAtHome { get => _pauseAtHome; set => Set(ref _pauseAtHome, value); }

        private string _status = "";
        public string Status { get => _status; private set => Set(ref _status, value); }

        public ICommand ApplyCommand { get; }

        // Bound to the OK button — caller (window) closes on true return.
        public Func<bool> OkAction { get; }

        public AdvancedSettingsViewModel(MainViewModel main)
        {
            _main = main;
            ApplyCommand = new RelayCommand(() => Apply());
            OkAction = () => Apply();
            Load();
        }

        private void Load()
        {
            int pierIdx = SettingToIndex(DriverSettings.PreferredPierSide);
            try { pierIdx = (int)_mount.Protocol.GetPreferredPierSide(); }
            catch { /* fall back */ }
            PreferredPierIndex = pierIdx;
            PauseAtHome = DriverSettings.PauseAtHomeOnFlip;
        }

        public bool Apply()
        {
            DriverSettings.PreferredPierSide = IndexToSetting(PreferredPierIndex);
            DriverSettings.PauseAtHomeOnFlip = PauseAtHome;
            try
            {
                if (_main.State == ConnState.Connected)
                {
                    var pierEnum = (LX200Protocol.PreferredPier)PreferredPierIndex;
                    bool ok1 = _mount.Protocol.SetPreferredPierSide(pierEnum);
                    bool ok2 = _mount.Protocol.SetPauseAtHomeOnFlip(PauseAtHome);
                    Status = (ok1 && ok2) ? "Applied to mount." : "Some commands rejected.";
                }
                else
                {
                    Status = "Saved (offline). Will reapply on next connect.";
                }
                return true;
            }
            catch (Exception ex)
            {
                Status = "Mount error: " + ex.Message;
                return false;
            }
        }

        private static int SettingToIndex(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            switch (char.ToUpperInvariant(s[0]))
            {
                case 'E': return 1;
                case 'W': return 2;
                case 'A': return 3;
                default:  return 0;
            }
        }

        private static string IndexToSetting(int i)
        {
            switch (i)
            {
                case 1: return "E";
                case 2: return "W";
                case 3: return "A";
                default: return "B";
            }
        }
    }
}
