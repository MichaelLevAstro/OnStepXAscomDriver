using System;
using Avalonia.Threading;
using ASCOM.OnStepX.Platform;

namespace ASCOM.OnStepX.Hub.Platform
{
    // IUiDispatcher over Avalonia's Dispatcher. A near-exact analogue of the
    // WPF one: Post is a queued invoke, Send runs inline when already on the UI
    // thread and blocks otherwise.
    internal sealed class AvaloniaDispatcher : IUiDispatcher
    {
        public bool OnUiThread => Dispatcher.UIThread.CheckAccess();

        public void Post(Action action)
        {
            if (action == null) return;
            try { Dispatcher.UIThread.Post(action); } catch { }
        }

        public void PostBackground(Action action)
        {
            if (action == null) return;
            try { Dispatcher.UIThread.Post(action, DispatcherPriority.Background); } catch { }
        }

        public void Send(Action action)
        {
            if (action == null) return;
            if (Dispatcher.UIThread.CheckAccess()) { action(); return; }
            try { Dispatcher.UIThread.Invoke(action); } catch { }
        }
    }
}
