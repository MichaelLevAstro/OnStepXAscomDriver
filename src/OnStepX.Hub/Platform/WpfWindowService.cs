using System;
using System.Threading.Tasks;
using System.Windows;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Diagnostics;
using ASCOM.OnStepX.Hardware.Transport;
using ASCOM.OnStepX.Platform;
using ASCOM.OnStepX.ViewModels;
using ASCOM.OnStepX.Views;

namespace ASCOM.OnStepX.Hub.Platform
{
    // The six child windows, opened exactly as the view models opened them:
    // owned by the main window, modal, blocking.
    internal sealed class WpfWindowService : IWindowService
    {
        public void ShowFocuserAdvanced(FocuserViewModel vm)
            => ShowModal(() => new FocuserAdvancedWindow(vm));

        public void ShowRotatorAdvanced(RotatorViewModel vm)
            => ShowModal(() => new RotatorAdvancedWindow(vm));

        public void ShowPolarAlignmentAdvanced(PolarAlignmentViewModel vm)
            => ShowModal(() => new PolarAlignmentAdvancedWindow(vm));

        public void ShowSlewTarget(MainViewModel main)
            => ShowModal(() => new SlewTargetWindow(main));

        public void ShowUpdateDialog(UpdateViewModel vm)
            => ShowModal(() => new UpdateDialog(vm));

        public Task<Site> ShowSitesManagerAsync(MainViewModel main, bool connected)
        {
            Site applied = null;
            try
            {
                var dlg = new SitesWindow(main, connected) { Owner = Owner };
                if (dlg.ShowDialog() == true) applied = dlg.AppliedSite;
            }
            catch (Exception ex)
            {
                DebugLogger.LogException("UI", ex);
            }
            // Already finished: ShowDialog blocks. Awaiting this resumes inline,
            // so the caller behaves as it did when it called ShowDialog itself.
            return Task.FromResult(applied);
        }

        private static Window Owner => Application.Current?.MainWindow;

        private static void ShowModal(Func<Window> create)
        {
            try
            {
                var w = create();
                w.Owner = Owner;
                w.ShowDialog();
            }
            catch (Exception ex)
            {
                DebugLogger.LogException("UI", ex);
                try { TransportLogger.Note("Dialog open failed: " + ex.Message); } catch { }
            }
        }
    }
}
