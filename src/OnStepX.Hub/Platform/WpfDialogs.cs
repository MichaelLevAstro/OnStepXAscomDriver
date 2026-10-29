using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ASCOM.OnStepX.Platform;
using ASCOM.OnStepX.Views;

namespace ASCOM.OnStepX.Hub.Platform
{
    // IDialogs over WPF's MessageBox — the calls the view models used to make
    // themselves, moved behind the interface and otherwise unchanged.
    //
    // The async methods complete synchronously, because MessageBox.Show blocks
    // until dismissed. That matters: every `await Ui.Dialogs.ConfirmAsync(...)`
    // in the view models therefore resumes inline, so the code after it runs in
    // the same order it always did. Avalonia's dialogs will not have that
    // property and those call sites will become genuinely asynchronous.
    //
    // No owner window is passed, which is also what the old call sites did —
    // the boxes centre on the screen rather than on the hub.
    internal sealed class WpfDialogs : IDialogs
    {
        public void Alert(string title, string message, DialogSeverity severity)
        {
            OnUi(() => MessageBox.Show(message, title, MessageBoxButton.OK, Icon(severity)));
        }

        public void AlertDetail(string title, string details)
        {
            OnUi(() => CopyableMessage.Show(title, details));
        }

        public Task<bool> ConfirmAsync(string title, string message, DialogSeverity severity,
                                       DialogButtons buttons, bool defaultAffirmative)
        {
            var button = buttons == DialogButtons.OkCancel ? MessageBoxButton.OKCancel : MessageBoxButton.YesNo;
            var affirmative = buttons == DialogButtons.OkCancel ? MessageBoxResult.OK : MessageBoxResult.Yes;
            var fallback = buttons == DialogButtons.OkCancel ? MessageBoxResult.Cancel : MessageBoxResult.No;
            var dflt = defaultAffirmative ? affirmative : fallback;

            var result = OnUi(() => MessageBox.Show(message, title, button, Icon(severity), dflt));
            return Task.FromResult(result == affirmative);
        }

        public Task<bool?> ConfirmCancelAsync(string title, string message, DialogSeverity severity)
        {
            var result = OnUi(() => MessageBox.Show(message, title, MessageBoxButton.YesNoCancel, Icon(severity)));
            bool? answer = result == MessageBoxResult.Yes ? true
                         : result == MessageBoxResult.No ? (bool?)false
                         : null;
            return Task.FromResult(answer);
        }

        // SyncLimitGuard's prompt. Called from the mount transport gate, which
        // may be the poll thread, an ASCOM client's pipe thread, or the UI
        // thread itself when the console's command box sent the sync.
        //
        // Unlike the others this one takes an owner, because it is interrupting
        // mount traffic and should sit over the hub rather than wherever the
        // mouse happens to be.
        public bool ConfirmBlocking(string title, string message, DialogSeverity severity, bool fallback)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null) return fallback;

            try
            {
                Func<bool> ask = () =>
                {
                    var owner = Application.Current?.MainWindow;
                    var result = owner != null && owner.IsLoaded
                        ? MessageBox.Show(owner, message, title, MessageBoxButton.OKCancel,
                                          Icon(severity), MessageBoxResult.Cancel)
                        : MessageBox.Show(message, title, MessageBoxButton.OKCancel,
                                          Icon(severity), MessageBoxResult.Cancel);
                    return result == MessageBoxResult.OK;
                };

                return d.CheckAccess() ? ask() : d.Invoke(ask);
            }
            catch
            {
                return fallback;
            }
        }

        private static MessageBoxImage Icon(DialogSeverity severity)
        {
            switch (severity)
            {
                case DialogSeverity.Error: return MessageBoxImage.Error;
                case DialogSeverity.Warning: return MessageBoxImage.Warning;
                case DialogSeverity.Question: return MessageBoxImage.Question;
                default: return MessageBoxImage.Information;
            }
        }

        private static void OnUi(Action action)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null || d.CheckAccess()) { action(); return; }
            d.Invoke(action);
        }

        private static T OnUi<T>(Func<T> func)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null || d.CheckAccess()) return func();
            return d.Invoke(func);
        }
    }
}
