using System.Threading.Tasks;

namespace ASCOM.OnStepX.Platform
{
    public enum DialogSeverity { Information, Question, Warning, Error }

    // Which pair of buttons a decision dialog offers. Kept because "OK /
    // Cancel" and "Yes / No" read differently to the user and the existing
    // call sites were deliberate about which one they asked.
    public enum DialogButtons { OkCancel, YesNo }

    // Replaces the direct MessageBox.Show calls the view models used to make.
    // WPF has a blocking MessageBox; Avalonia has no such thing, so the shapes
    // here are the ones that can be implemented on both.
    //
    // Alert is deliberately fire-and-forget. Twenty-seven of the thirty-six
    // original call sites were OK-only information boxes whose result nothing
    // read, and making those await would have turned every command handler
    // async for no gain.
    public interface IDialogs
    {
        void Alert(string title, string message, DialogSeverity severity = DialogSeverity.Information);

        // Same, but for text the user is likely to want to send on — an
        // exception dump, a diagnostics table. Shown selectable and copyable.
        void AlertDetail(string title, string details);

        // defaultAffirmative is which button has focus when the dialog opens.
        // It is false here and true at the OK/Cancel call sites because that is
        // what MessageBox did: the destructive Yes/No prompts all passed
        // MessageBoxResult.No explicitly, and OK/Cancel defaults to OK.
        Task<bool> ConfirmAsync(string title, string message,
                                DialogSeverity severity = DialogSeverity.Question,
                                DialogButtons buttons = DialogButtons.YesNo,
                                bool defaultAffirmative = false);

        // Three-state. null = the user cancelled rather than answering.
        Task<bool?> ConfirmCancelAsync(string title, string message,
                                       DialogSeverity severity = DialogSeverity.Question);

        // Blocks the calling thread until the user answers.
        //
        // Only SyncLimitGuard uses this, and only because it sits inside
        // MountSession's transport gate: the decision is whether a :CM# sync
        // reaches the wire, so it has to be made before the call returns.
        // The implementation has to cope with being called both from a worker
        // thread (the poll loop, the ASCOM pipe) and from the UI thread (the
        // console's manual command box).
        //
        // An implementation that cannot safely show a modal from where it was
        // called returns fallback rather than blocking. The caller decides what
        // that means — SyncLimitGuard passes false, because refusing a sync it
        // could not ask about is safer than letting it through.
        bool ConfirmBlocking(string title, string message, DialogSeverity severity, bool fallback);
    }
}
