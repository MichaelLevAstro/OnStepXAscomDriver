using System.Threading.Tasks;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Platform
{
    // The six child windows a view model can ask for. Each one is a view, so
    // the host project owns the type; the view model only names the window it
    // wants and hands over the model it should bind to.
    //
    // Owner, modality and placement are the implementation's business — every
    // one of these was `new SomeWindow(vm) { Owner = Application.Current
    // ?.MainWindow }; dlg.ShowDialog();` before.
    // internal rather than public because ShowSitesManagerAsync hands back a
    // Site, which is internal — and the hub, the only implementer, sees Core's
    // internals anyway.
    internal interface IWindowService
    {
        void ShowFocuserAdvanced(FocuserViewModel vm);

        void ShowRotatorAdvanced(RotatorViewModel vm);

        void ShowPolarAlignmentAdvanced(PolarAlignmentViewModel vm);

        void ShowSlewTarget(MainViewModel main);

        void ShowUpdateDialog(UpdateViewModel vm);

        // The one that answers back: the site the user chose to apply, or null
        // if they closed without applying. Async because Avalonia's ShowDialog
        // cannot be waited on synchronously from the UI thread.
        Task<Site> ShowSitesManagerAsync(MainViewModel main, bool connected);
    }
}
