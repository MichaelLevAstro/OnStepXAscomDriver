using System;
using System.Collections.Generic;

namespace ASCOM.OnStepX.ViewModels
{
    // Where RelayCommand gets its CanExecuteChanged from.
    //
    // WPF's CommandManager does two things at once: it raises RequerySuggested
    // when code asks it to, and it also raises it on its own after keyboard and
    // mouse input, which is why a button bound to a command whose CanExecute
    // reads a text box enables itself as you type. Avalonia has no equivalent,
    // so the host supplies this and the WPF host forwards to CommandManager —
    // which keeps the Windows hub behaving exactly as it did.
    public interface ICommandRequerySource
    {
        void Subscribe(EventHandler handler);
        void Unsubscribe(EventHandler handler);
        void RequestRequery();
    }

    public static class CommandRequery
    {
        private static ICommandRequerySource _source = new WeakEventSource();

        public static ICommandRequerySource Source => _source;

        public static void Install(ICommandRequerySource source)
        {
            if (source != null) _source = source;
        }

        // What the view models' InvalidateRequerySuggested calls became.
        public static void Invalidate() => _source.RequestRequery();

        // Default for hosts with no native requery of their own. Weak, because
        // commands belonging to a dialog's view model would otherwise be rooted
        // for the life of the process every time that dialog is opened.
        private sealed class WeakEventSource : ICommandRequerySource
        {
            private readonly List<WeakReference<EventHandler>> _handlers = new List<WeakReference<EventHandler>>();
            private readonly object _gate = new object();

            public void Subscribe(EventHandler handler)
            {
                if (handler == null) return;
                lock (_gate) _handlers.Add(new WeakReference<EventHandler>(handler));
            }

            public void Unsubscribe(EventHandler handler)
            {
                if (handler == null) return;
                lock (_gate)
                {
                    for (int i = _handlers.Count - 1; i >= 0; i--)
                        if (!_handlers[i].TryGetTarget(out var h) || h == handler)
                            _handlers.RemoveAt(i);
                }
            }

            public void RequestRequery()
            {
                EventHandler[] live;
                lock (_gate)
                {
                    var keep = new List<EventHandler>(_handlers.Count);
                    for (int i = _handlers.Count - 1; i >= 0; i--)
                    {
                        if (_handlers[i].TryGetTarget(out var h)) keep.Add(h);
                        else _handlers.RemoveAt(i);
                    }
                    live = keep.ToArray();
                }

                foreach (var h in live)
                {
                    try { h(null, EventArgs.Empty); } catch { }
                }
            }
        }
    }
}
