using System.Windows.Input;

namespace ASCOM.OnStepX.ViewModels
{
    // Rotator advanced popup VM. Wraps the derotator (parallactic-tracking)
    // controls. Buttons forward straight to RotatorViewModel commands;
    // DerotEnabled writes through immediately (matches existing inline behavior).
    public sealed class RotatorAdvancedViewModel : ViewModelBase
    {
        private readonly RotatorViewModel _rotator;

        public bool IsDerotateCapable => _rotator.IsDerotateCapable;

        public bool DerotEnabled
        {
            get => _rotator.DerotEnabled;
            set => _rotator.DerotEnabled = value;
        }

        public ICommand ToggleDerotReverseCommand => _rotator.ToggleDerotReverseCommand;
        public ICommand ParallacticCommand        => _rotator.ParallacticCommand;

        private string _status = "";
        public string Status { get => _status; private set => Set(ref _status, value); }

        public RotatorAdvancedViewModel(RotatorViewModel rotator) { _rotator = rotator; }

        // OK button just closes — there's nothing buffered to commit; the
        // checkbox and buttons flow straight through.
        public bool Apply() => true;
    }
}
