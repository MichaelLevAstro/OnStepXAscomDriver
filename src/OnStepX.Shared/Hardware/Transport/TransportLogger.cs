using System;

namespace ASCOM.OnStepX.Hardware.Transport
{
    // Tap point for observing every command/reply pair without coupling transports to the UI.
    // Implementations fire TX before the write and RX after the read. Subscribers (the hub
    // log panel, tests, external tools) attach to Line and render entries however they like.
    internal static class TransportLogger
    {
        public static event Action<string> Line;
        // Paired command+reply event. Fires once per completed SendAndReceive so UI
        // can render "cmd -> reply" on a single line without having to correlate
        // separate Tx/Rx entries. Blind sends produce a Pair with reply = "(blind)".
        public static event Action<string, string, int> Pair;

        public static void Tx(string cmd)
        {
            if (Line == null) return;
            Line.Invoke("-> " + Sanitize(cmd));
        }

        public static void Rx(string reply, int elapsedMs)
        {
            if (Line == null) return;
            Line.Invoke("<- " + Sanitize(reply) + "  (" + elapsedMs + " ms)");
        }

        public static void PairEvent(string cmd, string reply, int elapsedMs)
        {
            Pair?.Invoke(Sanitize(cmd), Sanitize(reply), elapsedMs);
        }

        public static void BlindEvent(string cmd)
        {
            Pair?.Invoke(Sanitize(cmd), "(blind)", 0);
        }

        public static void Note(string msg)
        {
            if (Line == null) return;
            Line.Invoke("-- " + msg);
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(empty)";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '\r') sb.Append("\\r");
                else if (c == '\n') sb.Append("\\n");
                else if (c < 0x20 || c >= 0x7F) sb.AppendFormat("\\x{0:X2}", (int)c & 0xFF);
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
