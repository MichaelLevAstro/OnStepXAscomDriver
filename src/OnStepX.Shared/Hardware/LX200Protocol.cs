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
        // NOTE on sign convention: LX200 :Gg/:Sg reports/accepts longitude as positive-WEST
        // (Meade classic). ASCOM and modern geo APIs use positive-EAST. The driver works
        // internally in ASCOM (east-positive) degrees; conversions happen here at the wire.
        public string GetLatitude()  => _transport.SendAndReceive(":Gt#");
        public string GetLongitudeRaw() => _transport.SendAndReceive(":Gg#");
        // East-positive longitude for the rest of the driver.
        public double GetLongitudeEastPositive()
        {
            if (CoordFormat.TryParseDegrees(GetLongitudeRaw(), out var westPos))
                return -westPos;
            throw new FormatException("Mount longitude reply could not be parsed");
        }
        public bool TryGetLongitudeEastPositive(out double eastPos)
        {
            if (CoordFormat.TryParseDegrees(GetLongitudeRaw(), out var westPos)) { eastPos = -westPos; return true; }
            eastPos = 0; return false;
        }
        public string GetElevation() => _transport.SendAndReceive(":Gv#");
        public string GetUtcOffset() => _transport.SendAndReceive(":GG#");
        public string GetDate() => _transport.SendAndReceive(":GC#");
        public string GetLocalTime() => _transport.SendAndReceive(":GL#");

        public bool SetLatitude(double deg)  => Bool(_transport.SendAndReceive(":St" + CoordFormat.FormatDegreesMount(deg) + "#"));
        // Input is east-positive; mount expects west-positive, so flip sign before formatting.
        public bool SetLongitude(double eastPositiveDeg) =>
            Bool(_transport.SendAndReceive(":Sg" + CoordFormat.FormatLongitudeHighPrec(-eastPositiveDeg) + "#"));
        public bool SetElevation(double m)
        {
            string v = (m >= 0 ? "+" : "") + m.ToString("0.0", CultureInfo.InvariantCulture);
            return Bool(_transport.SendAndReceive(":Sv" + v + "#"));
        }
        public bool SetUtcOffset(double hours)
        {
            int h = (int)Math.Truncate(hours);
            int mm = (int)Math.Abs((hours - h) * 60);
            string sgn = hours < 0 ? "-" : "+";
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

        // ---------- Slew / motion ----------
        // Returns 0 on success, otherwise a Meade-style error code.
        public int SlewToTarget()   => NumericSlew(_transport.SendAndReceive(":MS#"));
        public int SlewToTargetAltAz() => NumericSlew(_transport.SendAndReceive(":MA#"));
        public bool Sync()          => Bool(_transport.SendAndReceive(":CS#"));
        public void AbortSlew()     => _transport.SendBlind(":Q#");

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
        public bool ResetHomeHere()=> Bool(_transport.SendAndReceive(":hF#")); // OnStepX uses same family; wrapper kept for UI semantics.

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

        // ---------- Slew rate query ----------
        // Returns mount's configured max move-axis slew rate in deg/sec, or 0 if the
        // firmware doesn't support the query (pre-OnStepX or older builds).
        public double GetMaxSlewRateDegPerSec()
        {
            var s = _transport.SendAndReceive(":GX9A#");
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
