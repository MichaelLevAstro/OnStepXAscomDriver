using System;
using System.Collections.ObjectModel;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Diagnostics;
using ASCOM.OnStepX.Hardware;
using ASCOM.OnStepX.Hardware.Transport;

namespace ASCOM.OnStepX.ViewModels
{
    // Pier / meridian-flip policy. Lives in a section on the Advanced tab.
    // Replaces the old AdvancedSettingsWindow modal — preferred-pier and
    // pause-at-home commit live (no Apply/OK button) for parity with the
    // other inline Advanced controls.
    public sealed class PierMeridianViewModel : ViewModelBase
    {
        private readonly MainViewModel _main;
        private readonly MountSession _mount = MountSession.Instance;
        private bool _suppressApply;

        public ObservableCollection<string> PierOptions { get; } = new ObservableCollection<string>
        {
            "Best (stay on current side)",
            "East",
            "West",
            "Auto",
        };

        private int _preferredPierIndex;
        public int PreferredPierIndex
        {
            get => _preferredPierIndex;
            set
            {
                value = Math.Max(0, Math.Min(3, value));
                if (!Set(ref _preferredPierIndex, value)) return;
                if (_suppressApply) return;
                DriverSettings.PreferredPierSide = IndexToSetting(value);
                ApplyToMount();
            }
        }

        private bool _pauseAtHome;
        public bool PauseAtHome
        {
            get => _pauseAtHome;
            set
            {
                if (!Set(ref _pauseAtHome, value)) return;
                if (_suppressApply) return;
                DriverSettings.PauseAtHomeOnFlip = value;
                ApplyToMount();
            }
        }

        public PierMeridianViewModel(MainViewModel main)
        {
            _main = main;
            Load();
        }

        private void Load()
        {
            _suppressApply = true;
            try
            {
                int pierIdx = SettingToIndex(DriverSettings.PreferredPierSide);
                try { pierIdx = (int)_mount.Protocol.GetPreferredPierSide(); }
                catch { /* offline → registry default */ }
                PreferredPierIndex = pierIdx;
                PauseAtHome = DriverSettings.PauseAtHomeOnFlip;
            }
            finally { _suppressApply = false; }
        }

        private void ApplyToMount()
        {
            if (_main.State != ConnState.Connected) return;
            try
            {
                var pierEnum = (LX200Protocol.PreferredPier)_preferredPierIndex;
                _mount.Protocol.SetPreferredPierSide(pierEnum);
                _mount.Protocol.SetPauseAtHomeOnFlip(_pauseAtHome);
            }
            catch (Exception ex) { TransportLogger.Note("PierMeridian apply failed: " + ex.Message); }
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
