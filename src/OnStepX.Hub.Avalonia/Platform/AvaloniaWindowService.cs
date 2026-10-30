using System.Threading.Tasks;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Platform;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Hub.Platform
{
    // The six child windows, none of which are ported yet.
    //
    // They say so rather than doing nothing. A silent no-op is the failure
    // mode that let the update-banner binding sit broken through a whole
    // review, and an unported button that looks like it worked is worse than
    // one that admits it did not.
    internal sealed class AvaloniaWindowService : IWindowService
    {
        private readonly IDialogs _dialogs;

        public AvaloniaWindowService(IDialogs dialogs) => _dialogs = dialogs;

        public void ShowFocuserAdvanced(FocuserViewModel vm) => NotYet("Focuser — Advanced");

        public void ShowRotatorAdvanced(RotatorViewModel vm) => NotYet("Rotator — Advanced");

        public void ShowPolarAlignmentAdvanced(PolarAlignmentViewModel vm) => NotYet("Polar Alignment — Advanced");

        public void ShowSlewTarget(MainViewModel main) => NotYet("Go To Target");

        public void ShowUpdateDialog(UpdateViewModel vm) => NotYet("Software Update");

        public Task<Site> ShowSitesManagerAsync(MainViewModel main, bool connected)
        {
            NotYet("Sites");
            return Task.FromResult<Site>(null);
        }

        private void NotYet(string what) => _dialogs.Alert(
            what,
            "This window has not been ported to the new interface yet.\r\n\r\n" +
            "Use the Windows hub for it until it has.",
            DialogSeverity.Information);
    }
}
