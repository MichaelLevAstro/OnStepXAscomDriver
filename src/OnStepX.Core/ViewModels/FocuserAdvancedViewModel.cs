using System;
using System.Windows.Input;

namespace ASCOM.OnStepX.ViewModels
{
    // Wraps the focuser temperature-compensation controls into a modal popup.
    // Edits live on a copy; OK / Apply commits via FocuserViewModel so all
    // existing TCF write-paths and validation are reused. Cancel discards.
    public sealed class FocuserAdvancedViewModel : ViewModelBase
    {
        private readonly FocuserViewModel _focuser;

        public bool TempCompAvailable => _focuser.TempCompAvailable;
        public string TemperatureText => _focuser.TemperatureText;

        private bool _tcfEnabled;
        public bool TcfEnabled { get => _tcfEnabled; set => Set(ref _tcfEnabled, value); }

        private double _tcfCoeff;
        public double TcfCoeff { get => _tcfCoeff; set => Set(ref _tcfCoeff, value); }

        private int _tcfDeadband;
        public int TcfDeadband { get => _tcfDeadband; set => Set(ref _tcfDeadband, value); }

        private string _status = "";
        public string Status { get => _status; private set => Set(ref _status, value); }

        public ICommand ApplyCommand { get; }

        public FocuserAdvancedViewModel(FocuserViewModel focuser)
        {
            _focuser = focuser;
            // Snapshot from the live VM. Avoids surprising the user if a poll
            // arrives mid-edit.
            TcfEnabled  = focuser.TempCompEnabled;
            TcfCoeff    = focuser.TcfCoeff;
            TcfDeadband = focuser.TcfDeadband;
            ApplyCommand = new RelayCommand(() => Apply());
        }

        public bool Apply()
        {
            try
            {
                _focuser.TcfCoeff = TcfCoeff;
                _focuser.TcfDeadband = TcfDeadband;
                if (_focuser.TempCompEnabled != TcfEnabled) _focuser.TempCompEnabled = TcfEnabled;
                if (_focuser.ApplyTcfCommand.CanExecute(null))
                    _focuser.ApplyTcfCommand.Execute(null);
                Status = "Applied.";
                return true;
            }
            catch (Exception ex) { Status = "Error: " + ex.Message; return false; }
        }
    }
}
