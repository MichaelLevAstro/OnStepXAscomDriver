using System;

namespace ASCOM.OnStepX.Platform
{
    // The UI thread, as much of it as the view models need. Replaces the
    // Application.Current?.Dispatcher.BeginInvoke / Invoke calls they used to
    // make directly.
    public interface IUiDispatcher
    {
        // True when the caller is already on the UI thread.
        bool OnUiThread { get; }

        // Queue for the UI thread and return immediately. The old
        // Dispatcher.BeginInvoke.
        void Post(Action action);

        // Queue at a priority below input and rendering — used by the console
        // drain so a burst of mount traffic cannot starve the window.
        void PostBackground(Action action);

        // Run on the UI thread and wait. The old Dispatcher.Invoke. Calling
        // this from the UI thread runs inline rather than deadlocking.
        void Send(Action action);
    }
}
