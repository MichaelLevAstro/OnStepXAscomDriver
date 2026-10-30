using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ASCOM.OnStepX.Hardware.Transport;
using ASCOM.OnStepX.Platform;
using ASCOM.OnStepX.Views;

namespace ASCOM.OnStepX.Hub.Platform
{
    // IDialogs over MessageDialog.
    //
    // The important difference from the WPF implementation is that nothing here
    // blocks. WPF's MessageBox.Show ran a nested message loop, so every
    // `await Ui.Dialogs.ConfirmAsync(...)` in the view models resumed inline
    // and the code after it ran before the user had answered anything else.
    // Avalonia's ShowDialog returns a Task and keeps pumping, so those awaits
    // are now genuinely asynchronous. That is the correct behaviour, and it is
    // the thing to re-read when a view model does work after a prompt —
    // MainViewModel.ReconcileSiteLocationOnConnect says so at its definition.
    internal sealed class AvaloniaDialogs : IDialogs
    {
        private static Window Owner =>
            Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d
                ? d.MainWindow
                : null;

        public void Alert(string title, string message, DialogSeverity severity)
            => ShowDetached(MessageDialog.Build(title, message, severity, "OK"));

        public void AlertDetail(string title, string details)
            => ShowDetached(MessageDialog.Build(title, "", DialogSeverity.Information, "Close", detail: details));

        public async Task<bool> ConfirmAsync(string title, string message, DialogSeverity severity,
                                             DialogButtons buttons, bool defaultAffirmative)
        {
            string yes = buttons == DialogButtons.OkCancel ? "OK" : "Yes";
            string no = buttons == DialogButtons.OkCancel ? "Cancel" : "No";
            var dlg = MessageDialog.Build(title, message, severity, yes, no, defaultIsPrimary: defaultAffirmative);
            var outcome = await ShowAsync(dlg);
            return outcome == DialogOutcome.Affirmative;
        }

        public async Task<bool?> ConfirmCancelAsync(string title, string message, DialogSeverity severity)
        {
            var dlg = MessageDialog.Build(title, message, severity, "Yes", "No", "Cancel");
            var outcome = await ShowAsync(dlg);
            return outcome == DialogOutcome.Affirmative ? true
                 : outcome == DialogOutcome.Negative ? (bool?)false
                 : null;
        }

        // SyncLimitGuard's prompt: the answer decides whether a :CM# reaches
        // the wire, so the caller cannot be allowed to continue without it.
        //
        // From a worker thread — the mount poll loop, or an ASCOM client's pipe
        // thread, which is where this normally comes from — that is safe: the
        // dialog is raised on the UI thread, which keeps pumping, and only the
        // caller's own thread waits.
        //
        // From the UI thread it is not. Avalonia's ShowDialog does not run a
        // nested loop the way MessageBox.Show did, so blocking here would stop
        // the very thread that has to draw and dismiss the dialog. The contract
        // on IDialogs covers this: return the fallback instead. SyncLimitGuard
        // passes false, so a sync typed into the hub's own command box is
        // refused rather than silently allowed — the same answer the WPF hub
        // gave whenever its prompt could not be shown.
        public bool ConfirmBlocking(string title, string message, DialogSeverity severity, bool fallback)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                try
                {
                    TransportLogger.Note(
                        "Sync limit prompt cannot block the UI thread under Avalonia; " +
                        "answering " + fallback + " without asking.");
                }
                catch { }
                return fallback;
            }

            try
            {
                var tcs = new TaskCompletionSource<bool>();
                Dispatcher.UIThread.Post(async () =>
                {
                    try
                    {
                        var dlg = MessageDialog.Build(title, message, severity, "OK", "Cancel",
                                                      defaultIsPrimary: false);
                        var outcome = await ShowAsync(dlg);
                        tcs.TrySetResult(outcome == DialogOutcome.Affirmative);
                    }
                    catch (Exception ex)
                    {
                        try { TransportLogger.Note("Sync limit prompt failed: " + ex.Message); } catch { }
                        tcs.TrySetResult(fallback);
                    }
                });
                return tcs.Task.GetAwaiter().GetResult();
            }
            catch
            {
                return fallback;
            }
        }

        private static async Task<DialogOutcome> ShowAsync(MessageDialog dlg)
        {
            var owner = Owner;
            if (owner != null) await dlg.ShowDialog(owner);
            else { dlg.Show(); await WaitForClose(dlg); }
            return dlg.Outcome;
        }

        // Fire-and-forget: used by Alert, whose result nothing reads.
        private static void ShowDetached(MessageDialog dlg)
        {
            void Open()
            {
                try
                {
                    var owner = Owner;
                    if (owner != null) _ = dlg.ShowDialog(owner);
                    else dlg.Show();
                }
                catch (Exception ex)
                {
                    try { TransportLogger.Note("Dialog failed: " + ex.Message); } catch { }
                }
            }

            if (Dispatcher.UIThread.CheckAccess()) Open();
            else Dispatcher.UIThread.Post(Open);
        }

        private static Task WaitForClose(Window w)
        {
            var tcs = new TaskCompletionSource<bool>();
            w.Closed += (_, __) => tcs.TrySetResult(true);
            return tcs.Task;
        }
    }
}
