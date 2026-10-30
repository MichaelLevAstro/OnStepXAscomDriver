using System;
using System.Windows;
using System.Windows.Threading;
using ASCOM.OnStepX.Platform;

namespace ASCOM.OnStepX.Hub.Platform
{
    // IUiDispatcher over the WPF Dispatcher. Each method is what the view
    // models called directly before, including the null-conditional: during
    // shutdown Application.Current goes away and the old code silently did
    // nothing, so this does too.
    internal sealed class WpfDispatcher : IUiDispatcher
    {
        private static Dispatcher D => Application.Current?.Dispatcher;

        public bool OnUiThread
        {
            get
            {
                var d = D;
                return d == null || d.CheckAccess();
            }
        }

        public void Post(Action action)
        {
            if (action == null) return;
            var d = D;
            if (d == null) return;
            try { d.BeginInvoke(action); } catch { }
        }

        public void PostBackground(Action action)
        {
            if (action == null) return;
            var d = D;
            if (d == null) return;
            try { d.BeginInvoke(action, DispatcherPriority.Background); } catch { }
        }

        public void Send(Action action)
        {
            if (action == null) return;
            var d = D;
            if (d == null) { action(); return; }
            if (d.CheckAccess()) { action(); return; }
            try { d.Invoke(action); } catch { }
        }
    }
}
