using System;
using System.Globalization;
using ASCOM.OnStepX.Hardware.Transport;

namespace ASCOM.OnStepX.Hardware
{
    internal sealed class LX200Protocol
    {
        private readonly ITransport _transport;
        public LX200Protocol(ITransport transport) { _transport = transport; }

        // ---------- Identity ----------
        public string GetVersionProduct() => _transport.SendAndReceive(":GVP#");
        public string GetVersionNumber() => _transport.SendAndReceive(":GVN#");
        public string GetVersionFull()   => _transport.SendAndReceive(":GVM#");

        // ---------- Coordinates ----------
        public string GetRA()  => _transport.SendAndReceive(":GRH#");
        public string GetDec() => _transport.SendAndReceive(":GDH#");
        public string GetAlt() => _transport.SendAndReceive(":GA#");
        public string GetAz()  => _transport.SendAndReceive(":GZ#");
        public string GetSiderealTime() => _transport.SendAndReceive(":GS#");
        public string GetPierSide() => _transport.SendAndReceive(":Gm#");
        public string GetStatus()   => _transport.SendAndReceive(":GU#");
        public string GetLastError() => _transport.SendAndReceive(":GE#");

        // ---------- Site ----------
        // ASCOM/civil callers use east-positive; Meade :Sg/:Gg wire is west-
        // positive. Negate at the wire. The H suffix forces high-precision
        // reply regardless of the mount's current precision mode.
        public string GetLatitude()  => _transport.SendAndReceive(":GtH#");
        public string GetLongitudeRaw() => _transport.SendAndReceive(":GgH#");
        public double GetLongitude()
        {
            if (CoordFormat.TryParseDegrees(GetLongitudeRaw(), out var westPos))
                return -westPos;
            throw new FormatException("Mount longitude reply could not be parsed");
        }
        public bool TryGetLongitude(out double eastPos)
        {
            if (CoordFormat.TryParseDegrees(GetLongitudeRaw(), out var westPos))
            {
                eastPos = -westPos;
                return true;
            }
            eastPos = 0;
            return false;
        }
        public string GetElevation() => _transport.SendAndReceive(":Gv#");
        public string GetUtcOffset() => _transport.SendAndReceive(":GG#");
        public string GetDate() => _transport.SendAndReceive(":GC#");
        public string GetLocalTime() => _transport.SendAndReceive(":GL#");

        // Try OnStepX-Extended signed-decimal first (full precision); fall back
        // to classic DMS if firmware rejects.
        public bool SetLatitude(double deg)
        {
            if (Bool(_transport.SendAndReceive(":St" + CoordFormat.FormatDegreesDecimal(deg) + "#")))
                return true;
            return Bool(_transport.SendAndReceive(":St" + CoordFormat.FormatDegreesMount(deg) + "#"));
        }
        // Caller passes east-positive (civil/ASCOM); flip to west-positive for
        // the Meade :Sg wire convention. Mirrors SetUtcOffset.
        public bool SetLongitude(double eastPositiveDeg)
        {
            double westPos = -eastPositiveDeg;
            if (Bool(_transport.SendAndReceive(":Sg" + CoordFormat.FormatLongitudeDecimal(westPos) + "#")))
                return true;
            return Bool(_transport.SendAndReceive(":Sg" + CoordFormat.FormatLongitudeHighPrec(westPos) + "#"));
        }
        public bool SetElevation(double m)
        {
            string v = (m >= 0 ? "+" : "") + m.ToString("0.0", CultureInfo.InvariantCulture);
            return Bool(_transport.SendAndReceive(":Sv" + v + "#"));
        }
        // :SG is west-positive on the wire. Caller passes east-positive; flip.
        public bool SetUtcOffset(double tzHoursEastPositive)
        {
            double westPos = -tzHoursEastPositive;
            int h = (int)Math.Truncate(westPos);
            int mm = (int)Math.Abs((westPos - h) * 60);
            string sgn = westPos < 0 ? "-" : "+";
            return Bool(_transport.SendAndReceive(string.Format(CultureInfo.InvariantCulture, ":SG{0}{1:00}:{2:00}#", sgn, Math.Abs(h), mm)));
        }
        public bool SetLocalTime(DateTime localTime) =>
            Bool(_transport.SendAndReceive(localTime.ToString(@"\:SLHH\:mm\:ss\#", CultureInfo.InvariantCulture)));
        public bool SetLocalDate(DateTime localDate) =>
            Bool(_transport.SendAndReceive(localDate.ToString(@"\:SCMM\/dd\/yy\#", CultureInfo.InvariantCulture)));

        // ---------- Targets ----------
        public bool SetTargetRA(double hours) =>
            Bool(_transport.SendAndReceive(":Sr" + CoordFormat.FormatHoursHighPrec(hours) + "#"));
        public bool SetTargetDec(double deg) =>
            Bool(_transport.SendAndReceive(":Sd" + CoordFormat.FormatDegreesMount(deg) + "#"));
        public bool SetTargetAlt(double deg) =>
            Bool(_transport.SendAndReceive(":Sa" + CoordFormat.FormatDegreesMount(deg) + "#"));
        public bool SetTargetAz(double deg) =>
            Bool(_transport.SendAndReceive(":Sz" + CoordFormat.FormatLongitudeHighPrec(deg) + "#"));
        // Read whatever target the firmware currently holds — set via :Sr/:Sd
        // through any path (ASCOM setter, raw command, prior client, hub UI).
        // Used by the sync guard so the check is path-agnostic.
        public string GetTargetRA()  => _transport.SendAndReceive(":Gr#");
        public string GetTargetDec() => _transport.SendAndReceive(":Gd#");

        // ---------- Slew / motion ----------
        // Returns 0 on success, otherwise a Meade-style error code.
        public int SlewToTarget()   => NumericSlew(_transport.SendAndReceive(":MS#"));
        public int SlewToTargetAltAz() => NumericSlew(_transport.SendAndReceive(":MA#"));
        // :CS# (Calibrate Sync) — runtime sync. Blind in OnStepX firmware
        // (no reply); errors surface via :GE#. Firmware re-derives the pier
        // flag from the synced HA when PIER_SIDE_SYNC_CHANGE_SIDES is ON in
        // its build, which can flip :Gm# without physical motion.
        public bool Sync()
        {
            _transport.SendBlind(":CS#");
            return true;
        }
        public void AbortSlew()     => _transport.SendBlind(":Q#");

        // :Q# is blind. Poll :GU# for 'N' (not slewing); resend and poll
        // again on first miss. Throws on persistent failure.
        public void AbortSlewVerified(int totalTimeoutMs = 3000, int pollMs = 150)
        {
            int half = Math.Max(500, totalTimeoutMs / 2);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                _transport.SendBlind(":Q#");
                int waited = 0;
                while (waited < half)
                {
                    System.Threading.Thread.Sleep(pollMs);
                    waited += pollMs;
                    string raw;
                    try { raw = _transport.SendAndReceive(":GU#"); }
                    catch { continue; } // Transient wire glitch — keep polling.
                    raw = (raw ?? "").TrimEnd('#');
                    // 'N' = not slewing; 'I' = park-in-progress (still moving).
                    if (raw.IndexOf('N') >= 0 && raw.IndexOf('I') < 0) return;
                }
            }
            throw new System.IO.IOException(
                "AbortSlew: mount still slewing after " + totalTimeoutMs + "ms — " +
                ":Q# may not have reached the firmware. Power-cycle if motion continues.");
        }

        public void MoveNorth() => _transport.SendBlind(":Mn#");
        public void MoveSouth() => _transport.SendBlind(":Ms#");
        public void MoveEast()  => _transport.SendBlind(":Me#");
        public void MoveWest()  => _transport.SendBlind(":Mw#");
        public void StopNorth() => _transport.SendBlind(":Qn#");
        public void StopSouth() => _transport.SendBlind(":Qs#");
        public void StopEast()  => _transport.SendBlind(":Qe#");
        public void StopWest()  => _transport.SendBlind(":Qw#");

        public bool PulseGuide(char dir, int ms)
        {
            // :Mg<d><ms>#  d ∈ {n,s,e,w}; ms 1..9999
            _transport.SendBlind(string.Format(CultureInfo.InvariantCulture, ":Mg{0}{1:0000}#", dir, Math.Max(1, Math.Min(9999, ms))));
            return true;
        }

        // Slew rate preset 1..5
        public bool SetSlewRatePreset(int level) =>
            Bool(_transport.SendAndReceive(string.Format(CultureInfo.InvariantCulture, ":SX93,{0}#", Math.Max(1, Math.Min(5, level)))));

        // MoveAxis rates (deg/sec on each axis)
        public void SetMoveAxisRateRA(double degPerSec)  => _transport.SendBlind(string.Format(CultureInfo.InvariantCulture, ":RA{0:0.000}#", degPerSec));
        public void SetMoveAxisRateDec(double degPerSec) => _transport.SendBlind(string.Format(CultureInfo.InvariantCulture, ":RE{0:0.000}#", degPerSec));

        // ---------- Tracking ----------
        public bool TrackingOn()  => Bool(_transport.SendAndReceive(":Te#"));
        public bool TrackingOff() => Bool(_transport.SendAndReceive(":Td#"));
        public double GetTrackingRateHz()
        {
            var s = _transport.SendAndReceive(":GT#");
            double.TryParse(Strip(s), NumberStyles.Float, CultureInfo.InvariantCulture, out var v);
            return v;
        }

        // Tracking rate type — OnStepX blind commands (no reply)
        public void SetTrackingSidereal() => _transport.SendBlind(":TQ#");
        public void SetTrackingSolar()    => _transport.SendBlind(":TS#");
        public void SetTrackingLunar()    => _transport.SendBlind(":TL#");
        public void SetTrackingKing()     => _transport.SendBlind(":TK#");

        // ---------- Park / Home ----------
        public bool Park()         => Bool(_transport.SendAndReceive(":hP#"));
        public bool Unpark()       => Bool(_transport.SendAndReceive(":hR#"));
        public bool SetParkHere()  => Bool(_transport.SendAndReceive(":hQ#"));
        public void FindHome()     => _transport.SendBlind(":hF#");
        public void GoHome()       => _transport.SendBlind(":hC#");

        // ---------- Limits ----------
        public int GetHorizonLimit()
        {
            int.TryParse(Digits(_transport.SendAndReceive(":Gh#")), out var v); return v;
        }
        public int GetOverheadLimit()
        {
            int.TryParse(Digits(_transport.SendAndReceive(":Go#")), out var v); return v;
        }
        public bool SetHorizonLimit(int deg)
        {
            string s = (deg < 0 ? "-" : "+") + Math.Abs(deg).ToString("00", CultureInfo.InvariantCulture);
            return Bool(_transport.SendAndReceive(":Sh" + s + "#"));
        }
        public bool SetOverheadLimit(int deg) =>
            Bool(_transport.SendAndReceive(":So" + Math.Abs(deg).ToString("00", CultureInfo.InvariantCulture) + "#"));

        // ---------- Meridian flip policy ----------
        public bool SetMeridianAutoFlip(bool on) =>
            Bool(_transport.SendAndReceive(":SX95," + (on ? "1" : "0") + "#"));
        public bool GetMeridianAutoFlip()
        {
            var s = _transport.SendAndReceive(":GX95#");
            return Strip(s).StartsWith("1");
        }

        // Preferred pier side. OnStepX :SX96 accepts single-char values:
        //   'B' = Best (stay on current side when possible — recommended for
        //          plate-solve workflows near the meridian),
        //   'E' = prefer East pier, 'W' = prefer West pier, 'A' = Auto.
        // :GX96 reply is one of 'E','W','B','A' (or 'E' if meridian flips
        // disabled upstream — firmware compat fallback).
        public enum PreferredPier { Best, East, West, Auto }
        private static char PreferredPierToChar(PreferredPier p) => p switch
        {
            PreferredPier.East => 'E',
            PreferredPier.West => 'W',
            PreferredPier.Auto => 'A',
            _ => 'B',
        };
        private static PreferredPier CharToPreferredPier(char c) => c switch
        {
            'E' => PreferredPier.East,
            'W' => PreferredPier.West,
            'A' => PreferredPier.Auto,
            _ => PreferredPier.Best,
        };
        public bool SetPreferredPierSide(PreferredPier p) =>
            Bool(_transport.SendAndReceive(":SX96," + PreferredPierToChar(p) + "#"));
        public PreferredPier GetPreferredPierSide()
        {
            var s = Strip(_transport.SendAndReceive(":GX96#"));
            if (string.IsNullOrEmpty(s)) return PreferredPier.Best;
            return CharToPreferredPier(char.ToUpperInvariant(s[0]));
        }

        // :SX98 is write-only; state mirrored in driver settings.
        public bool SetPauseAtHomeOnFlip(bool on) =>
            Bool(_transport.SendAndReceive(":SX98," + (on ? "1" : "0") + "#"));

        // ---------- Admin / NV ----------
        public void ResetNvMemory() => _transport.SendBlind(":ENVRESET#");
        public void RebootMount()   => _transport.SendBlind(":ERESET#");

        // ---------- Meridian limits (minutes of RA past meridian) ----------
        // 1 min RA = 0.25°. Negative = stop tracking before meridian.
        public int GetMeridianLimitEastMinutes()
        {
            int.TryParse(Digits(_transport.SendAndReceive(":GXE9#")), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v);
            return v;
        }
        public int GetMeridianLimitWestMinutes()
        {
            int.TryParse(Digits(_transport.SendAndReceive(":GXEA#")), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v);
            return v;
        }
        public bool SetMeridianLimitEastMinutes(int minutes) =>
            Bool(_transport.SendAndReceive(string.Format(CultureInfo.InvariantCulture, ":SXE9,{0}#", minutes)));
        public bool SetMeridianLimitWestMinutes(int minutes) =>
            Bool(_transport.SendAndReceive(string.Format(CultureInfo.InvariantCulture, ":SXEA,{0}#", minutes)));

        // ---------- Slew rate query ----------
        // Returns mount's configured max move-axis slew rate in deg/sec, or 0 if the
        // firmware doesn't support the query (pre-OnStepX or older builds).
        // Slew rate is expressed in firmware as microseconds per motor step.
        // Rate is inversely proportional: rate_deg_s ∝ 1 / us_per_step.
        //   :GX92#  current us/step (settings.usPerStepCurrent)
        //   :GX93#  base    us/step (usPerStepBase, derived from SLEW_RATE_BASE_DESIRED)
        //   :GX97#  current step rate in deg/s
        //   :GX99#  fastest us/step (mechanical lower limit)
        // Set via :SX92,<us>#  — firmware clamps to [base/2, base*2] ∩ ≥ lower limit.
        public double GetUsPerStepCurrent() => ParseDouble(_transport.SendAndReceive(":GX92#"));
        public double GetUsPerStepBase()    => ParseDouble(_transport.SendAndReceive(":GX93#"));
        // :GX97# occasionally returns the product name ("On-Step#") on the first call
        // after a cold boot — likely a firmware quirk where a pending :GVP# reply gets
        // flushed into the next read. Retry until a numeric reply comes back. Safety
        // cap prevents hang if firmware genuinely doesn't implement the command.
        public double GetCurrentStepRateDegPerSec()
        {
            for (int i = 0; i < 10; i++)
            {
                var s = Strip(_transport.SendAndReceive(":GX97#"));
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    return v;
            }
            return 0.0;
        }
        public double GetUsPerStepLowerLimit() => ParseDouble(_transport.SendAndReceive(":GX99#"));

        // Base slew rate in deg/s, derived from the current-rate readback and the
        // us/step ratio. Any non-zero current rate works because rate ∝ 1/us_per_step.
        public double GetBaseSlewRateDegPerSec()
        {
            double curRate = GetCurrentStepRateDegPerSec();
            double usCur   = GetUsPerStepCurrent();
            double usBase  = GetUsPerStepBase();
            if (curRate <= 0 || usCur <= 0 || usBase <= 0) return 0.0;
            return curRate * (usCur / usBase);
        }

        public bool SetUsPerStepCurrent(double us) =>
            Bool(_transport.SendAndReceive(string.Format(CultureInfo.InvariantCulture, ":SX92,{0:0.000}#", us)));

        private static double ParseDouble(string s)
        {
            s = Strip(s);
            if (string.IsNullOrEmpty(s)) return 0.0;
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v);
            return v;
        }

        // ---------- Guide rate (custom) ----------
        public bool SetGuideRateMultiplier(double xSidereal) =>
            Bool(_transport.SendAndReceive(string.Format(CultureInfo.InvariantCulture, ":SX90,{0:0.00}#", xSidereal)));
        public double GetGuideRateMultiplier()
        {
            var s = _transport.SendAndReceive(":GX90#");
            double.TryParse(Strip(s), NumberStyles.Float, CultureInfo.InvariantCulture, out var v);
            return v;
        }

        // ---------- Parsers ----------
        private static bool Bool(string reply)
        {
            reply = Strip(reply);
            return !string.IsNullOrEmpty(reply) && reply[0] == '1';
        }

        private static int NumericSlew(string reply)
        {
            reply = Strip(reply);
            if (string.IsNullOrEmpty(reply)) return 23;
            if (reply[0] == '0') return 0;
            if (char.IsDigit(reply[0])) return reply[0] - '0';
            return 23;
        }

        private static string Strip(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Trim().TrimEnd('#');
        }

        private static string Digits(string s)
        {
            s = Strip(s);
            var sb = new System.Text.StringBuilder();
            bool sign = false;
            foreach (var c in s)
            {
                if ((c == '-' || c == '+') && !sign) { sb.Append(c); sign = true; }
                else if (char.IsDigit(c)) sb.Append(c);
                else if (sb.Length > 0) break;
            }
            return sb.ToString();
        }
    }

    [Flags]
    internal enum OnStepStatusFlags
    {
        None = 0,
        NotTracking     = 1 << 0,
        SlewingGeneral  = 1 << 1,
        Parking         = 1 << 2,
        Parked          = 1 << 3,
        PecRecording    = 1 << 4,
        AtHome          = 1 << 5,
        WaitingAtHome   = 1 << 6,
        PauseAtHome     = 1 << 7,
    }
}
