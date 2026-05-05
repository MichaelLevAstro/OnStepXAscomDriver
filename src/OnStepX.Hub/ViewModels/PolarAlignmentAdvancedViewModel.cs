using System;
using System.Windows.Input;
using ASCOM.OnStepX.Config;

namespace ASCOM.OnStepX.ViewModels
{
    // VM for the PA Advanced popup. Edits per-axis run current (mA) and
    // hold percent. Apply pushes :SXA4,IRUN= / :SXA5,IHOLD= etc. to mount
    // and persists to DriverSettings via PolarAlignmentViewModel.
    public sealed class PolarAlignmentAdvancedViewModel : ViewModelBase
    {
        private readonly PolarAlignmentViewModel _pa;

        private int _altRunMa;
        public int AltRunMa { get => _altRunMa; set => Set(ref _altRunMa, Math.Max(50, Math.Min(3000, value))); }
        private int _altHoldPct;
        public int AltHoldPct { get => _altHoldPct; set => Set(ref _altHoldPct, Math.Max(0, Math.Min(100, value))); }

        private int _azRunMa;
        public int AzRunMa { get => _azRunMa; set => Set(ref _azRunMa, Math.Max(50, Math.Min(3000, value))); }
        private int _azHoldPct;
        public int AzHoldPct { get => _azHoldPct; set => Set(ref _azHoldPct, Math.Max(0, Math.Min(100, value))); }

        private string _status = "";
        public string Status { get => _status; private set => Set(ref _status, value); }

        public ICommand ApplyCommand { get; }

        public PolarAlignmentAdvancedViewModel(PolarAlignmentViewModel pa)
        {
            _pa = pa;
            ApplyCommand = new RelayCommand(Apply);
            // Load current persisted values.
            _altRunMa   = DriverSettings.PolarAlignAltRunCurrent;
            _altHoldPct = DriverSettings.PolarAlignAltHoldPercent;
            _azRunMa    = DriverSettings.PolarAlignAzRunCurrent;
            _azHoldPct  = DriverSettings.PolarAlignAzHoldPercent;
        }

        public void Apply()
        {
            try
            {
                _pa.ApplyDriverCurrents(_altRunMa, _altHoldPct, _azRunMa, _azHoldPct);
                Status = "Applied to mount.";
            }
            catch (Exception ex)
            {
                Status = "Apply failed: " + ex.Message;
            }
        }
    }
}
