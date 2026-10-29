namespace ASCOM.OnStepX.Platform
{
    // The host application itself: its main window's chrome, its lifetime, and
    // the two things it can hand to the rest of the desktop. Replaces the
    // Application.Current.MainWindow / Application.Current.Shutdown /
    // Clipboard / Process.Start calls the view models used to make.
    public interface IShell
    {
        void Minimize();

        // Maximised <-> normal. The title bar's one button does both.
        void ToggleMaximize();

        // Closes the main window, which the hub treats as hide-to-tray rather
        // than exit — the window's own close handling decides, not this.
        void CloseMainWindow();

        // Ends the process. Used by the updater once the installer is staged.
        void Shutdown();

        // Dark <-> light. The themes themselves are resource dictionaries the
        // host owns, so the view model can only ask for the switch.
        void ToggleTheme();

        void SetClipboardText(string text);

        // Hands a folder path or a URL to the desktop: Explorer on Windows,
        // xdg-open on Linux, open(1) on macOS. Never throws.
        void OpenExternal(string pathOrUrl);
    }
}
