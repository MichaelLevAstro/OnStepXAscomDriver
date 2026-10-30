using System;

namespace ASCOM.OnStepX.Hardware.Transport
{
    // Which LX200 commands OnStep answers, and which it does not.
    //
    // The hub has never needed this: the ASCOM pipe protocol has the *client*
    // declare it (CMD versus BLIND), and LX200Protocol knows at every call site
    // whether it is calling SendAndReceive or SendBlind. A raw LX200 client on
    // the TCP bridge declares nothing, so the bridge has to decide.
    //
    // Getting it wrong in one direction is survivable and in the other is not:
    //
    //   Treating a silent command as one that replies costs a read that times
    //   out. The mount sent nothing, so nothing is left on the wire and the
    //   next exchange is still aligned. Slow, harmless.
    //
    //   Treating a replying command as silent leaves its reply unread, and
    //   every later read is then matched to the wrong command — permanently,
    //   until the link is reopened.
    //
    // So anything not known to be silent is treated as replying. The cost of
    // being wrong is a timeout; the cost of the opposite is a mount being
    // driven on stale readings.
    //
    // The list is taken from LX200Protocol's own SendBlind call sites, which
    // is what the hub itself relies on against real hardware.
    internal static class Lx200CommandSet
    {
        private static readonly string[] Exact =
        {
            // Motion stops and manual slews
            ":Q#", ":Qn#", ":Qs#", ":Qe#", ":Qw#",
            ":Mn#", ":Ms#", ":Me#", ":Mw#",

            // Tracking rate selection
            ":TQ#", ":TS#", ":TL#", ":TK#",

            // Home and park basis, and the destructive pair
            ":hC#", ":hF#", ":CS#",
            ":ENVRESET#", ":ERESET#",

            // Focuser motion and homing
            ":F+#", ":F-#", ":FQ#", ":FZ#", ":FH#", ":Fh#",

            // Rotator motion and homing
            ":r<#", ":r>#", ":rC#", ":rF#", ":rP#", ":rQ#", ":rR#", ":rZ#",
        };

        private static readonly string[] Prefixes =
        {
            ":Mg",   // guide pulse, :Mg<dir><ms>#
            ":Fr",   // focuser relative move
            ":Fs",   // focuser absolute move
            ":RA",   // axis 1 rate
            ":RE",   // axis 2 rate
            ":SXA",  // the write-only :SXA.. setters
        };

        // True when OnStep sends nothing back.
        public static bool IsBlind(string command)
        {
            if (string.IsNullOrEmpty(command)) return false;

            foreach (var c in Exact)
                if (string.Equals(command, c, StringComparison.Ordinal)) return true;

            foreach (var p in Prefixes)
                if (command.StartsWith(p, StringComparison.Ordinal)) return true;

            // Rate presets: a letter then digits, :r3# or :F5#. The lettered
            // forms of the same prefixes (:rA#, :rG#, :Fg#) are queries and
            // must not be caught here.
            if (command.Length >= 4 && command[0] == ':' && command[command.Length - 1] == '#')
            {
                char axis = command[1];
                if (axis == 'r' || axis == 'F')
                {
                    bool allDigits = true;
                    for (int i = 2; i < command.Length - 1; i++)
                        if (!char.IsDigit(command[i])) { allDigits = false; break; }
                    if (allDigits && command.Length > 3) return true;
                }
            }

            return false;
        }
    }
}
