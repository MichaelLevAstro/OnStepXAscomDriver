using System;
using System.Threading.Tasks;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.ViewModels;

namespace ASCOM.OnStepX.Platform
{
    // Where the view models reach the host application. Set once from the
    // host's entry point, before any view model is constructed.
    //
    // A static locator rather than constructor injection on purpose: the view
    // models are already built as a tree hung off MainViewModel, and the code
    // around them (MountSession.Instance, DriverSettings, NotificationService,
    // DebugLogger) is static too. Threading six interfaces through twenty
    // constructors would be a bigger change than the one being made here, and
    // a stranger-looking one next to its neighbours.
    //
    // Every slot starts as a no-op so a view model constructed in a test, or
    // before Install runs, degrades to doing nothing instead of throwing.
    public static class Ui
    {
        public static IDialogs Dialogs { get; private set; } = new NullDialogs();
        public static IUiDispatcher Dispatcher { get; private set; } = new InlineDispatcher();
        public static IShell Shell { get; private set; } = new NullShell();
        public static INotifier Notifier { get; private set; } = new NullNotifier();
        internal static IWindowService Windows { get; private set; } = new NullWindows();
        public static IFilePicker Files { get; private set; } = new NullFilePicker();
        public static ILocationProvider Location { get; private set; } = new NullLocation();
        public static ISettingsStore Settings { get; private set; } = new MemorySettingsStore();

        internal static void Install(
            IDialogs dialogs,
            IUiDispatcher dispatcher,
            IShell shell,
            INotifier notifier,
            IWindowService windows,
            ISettingsStore settings,
            IFilePicker files,
            ILocationProvider location)
        {
            if (dialogs != null) Dialogs = dialogs;
            if (dispatcher != null) Dispatcher = dispatcher;
            if (shell != null) Shell = shell;
            if (notifier != null) Notifier = notifier;
            if (windows != null) Windows = windows;
            if (settings != null) Settings = settings;
            if (files != null) Files = files;
            if (location != null) Location = location;
        }

        // ---- fallbacks -----------------------------------------------------

        private sealed class NullDialogs : IDialogs
        {
            public void Alert(string title, string message, DialogSeverity severity) { }

            public void AlertDetail(string title, string details) { }

            public Task<bool> ConfirmAsync(string title, string message, DialogSeverity severity, DialogButtons buttons, bool defaultAffirmative)
                => Task.FromResult(false);

            public Task<bool?> ConfirmCancelAsync(string title, string message, DialogSeverity severity)
                => Task.FromResult<bool?>(null);

            // No UI to ask with, so the guard proceeds — see IDialogs.
            public bool ConfirmBlocking(string title, string message, DialogSeverity severity, bool fallback)
                => fallback;
        }

        // Runs everything on the calling thread. Correct for a headless host
        // and harmless before a real dispatcher is installed.
        private sealed class InlineDispatcher : IUiDispatcher
        {
            public bool OnUiThread => true;
            public void Post(Action action) => action?.Invoke();
            public void PostBackground(Action action) => action?.Invoke();
            public void Send(Action action) => action?.Invoke();
        }

        private sealed class NullShell : IShell
        {
            public void Minimize() { }
            public void ToggleMaximize() { }
            public void CloseMainWindow() { }
            public void Shutdown() { }
            public void ToggleTheme() { }
            public void SetClipboardText(string text) { }
            public void OpenExternal(string pathOrUrl) { }
        }

        private sealed class NullNotifier : INotifier
        {
            public void Show(string title, string body, NotificationSeverity severity) { }
        }

        private sealed class NullWindows : IWindowService
        {
            public void ShowFocuserAdvanced(FocuserViewModel vm) { }
            public void ShowRotatorAdvanced(RotatorViewModel vm) { }
            public void ShowPolarAlignmentAdvanced(PolarAlignmentViewModel vm) { }
            public void ShowSlewTarget(MainViewModel main) { }
            public void ShowUpdateDialog(UpdateViewModel vm) { }
            public Task<Site> ShowSitesManagerAsync(MainViewModel main, bool connected) => Task.FromResult<Site>(null);
        }

        private sealed class NullFilePicker : IFilePicker
        {
            public Task<string> OpenAsync(string title, string filter) => Task.FromResult<string>(null);
            public Task<string> SaveAsync(string title, string filter, string suggestedName) => Task.FromResult<string>(null);
        }

        private sealed class NullLocation : ILocationProvider
        {
            public Task<LocationFix> GetAsync()
                => Task.FromResult(new LocationFix { Ok = false, Failure = "No location provider on this platform." });
        }

        private sealed class MemorySettingsStore : ISettingsStore
        {
            private readonly System.Collections.Generic.Dictionary<string, string> _v =
                new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public string Read(string name, string fallback)
                => _v.TryGetValue(name ?? "", out var s) ? s : fallback;

            public void Write(string name, string value) { _v[name ?? ""] = value; }
        }
    }
}
