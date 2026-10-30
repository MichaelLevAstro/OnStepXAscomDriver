namespace ASCOM.OnStepX.Platform
{
    public enum NotificationSeverity { Info, Warning, Error }

    // Desktop notification — a Windows toast today, libnotify on Linux and
    // osascript on macOS later. Distinct from IDialogs: a notification is
    // something the user may never look at, and nothing waits on it.
    //
    // Debouncing and the user's enable/disable preference are handled by
    // NotificationService in front of this, not by implementations.
    public interface INotifier
    {
        void Show(string title, string body, NotificationSeverity severity);
    }
}
