using System.Windows.Media;

namespace ASCOM.OnStepX.ViewModels
{
    public enum ConsoleEntryKind { Pair, Invalid, Note }

    public sealed class ConsoleEntry
    {
        public string Timestamp { get; set; }
        public string Command { get; set; }
        public string Response { get; set; }
        public string Elapsed { get; set; }
        public ConsoleEntryKind Kind { get; set; }

        // Source line text for filter matching (raw form mirrors the legacy
        // "ts  cmd  ->  reply  (Nms)" string so existing user filters work).
        public string Raw { get; set; }

        // True for blind responses ("(blind)") so the row template renders
        // them muted.
        public bool IsBlind { get; set; }
    }
}
