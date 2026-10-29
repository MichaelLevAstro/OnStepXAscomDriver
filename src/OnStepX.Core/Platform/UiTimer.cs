using System;

namespace ASCOM.OnStepX.Platform
{
    // Stands in for WPF's DispatcherTimer: a repeating timer whose Tick runs on
    // the UI thread, so handlers can touch bound properties directly.
    //
    // Built on System.Timers.Timer plus Ui.Dispatcher rather than on a toolkit
    // timer, so there is nothing to reimplement per platform.
    //
    // DispatcherTimer cannot re-enter — it posts to a single-threaded queue and
    // a slow handler simply delays the next tick. A threadpool timer can have
    // several ticks in flight, so _inTick drops any that arrive while a handler
    // is still running. That matches the behaviour the view models were written
    // against.
    public sealed class UiTimer : IDisposable
    {
        private readonly System.Timers.Timer _timer = new System.Timers.Timer { AutoReset = true };
        private int _inTick;

        public UiTimer()
        {
            _timer.Elapsed += (s, e) =>
            {
                if (System.Threading.Interlocked.Exchange(ref _inTick, 1) == 1) return;
                Ui.Dispatcher.Post(() =>
                {
                    try { Tick?.Invoke(this, EventArgs.Empty); }
                    catch { }
                    finally { System.Threading.Interlocked.Exchange(ref _inTick, 0); }
                });
            };
        }

        public event EventHandler Tick;

        public TimeSpan Interval
        {
            get => TimeSpan.FromMilliseconds(_timer.Interval);
            set => _timer.Interval = Math.Max(1, value.TotalMilliseconds);
        }

        public bool IsEnabled => _timer.Enabled;

        public void Start() => _timer.Start();

        public void Stop()
        {
            _timer.Stop();
            System.Threading.Interlocked.Exchange(ref _inTick, 0);
        }

        public void Dispose()
        {
            try { _timer.Stop(); _timer.Dispose(); } catch { }
        }
    }
}
