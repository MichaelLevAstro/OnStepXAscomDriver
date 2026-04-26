using System;
using System.Globalization;
using System.Windows.Forms;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Hardware;
using ASCOM.OnStepX.Hardware.State;
using ASCOM.OnStepX.Hardware.Transport;

namespace ASCOM.OnStepX.Notifications
{
    // Single chokepoint sync guard. Lives in MountSession.SendAndReceiveRaw /
    // SendBlindRaw — every :CM#-class sync command, regardless of origin
    // (driver via pipe, hub manual command box, future tools), funnels through
    // those methods, so gating here covers every path.
    //
    // Current position: read from MountStateCache (already polled by the
    // 750 ms poll loop, no extra round-trip).
    // Target position: queried fresh via :Gr#/:Gd# — exactly the value :CM#
    // will sync to, regardless of how the target was set.
    //
    // Over-limit UI: OK/Cancel MessageBox marshalled to the hub UI thread.
    // OK lets the sync through; Cancel blocks before the byte hits the wire.
    internal static class SyncLimitGuard
    {
        public static bool IsSyncCommand(string wire)
        {
            if (string.IsNullOrEmpty(wire)) return false;
            string s = wire.Trim();
            return s.Equals(":CM#", StringComparison.Ordinal)
                || s.Equals(":CMR#", StringComparison.Ordinal)
                || s.Equals(":CS#", StringComparison.Ordinal);
        }

        // Caller must already hold the MountSession transport gate around any
        // wire access we do here. Pass the live LX200Protocol so we reuse the
        // same transport without re-entering the gate.
        public static bool ShouldAllowSync(LX200Protocol protocol, MountStateCache state)
        {
            int limit = DriverSettings.SyncLimitDeg;
            if (limit <= 0) return true;
            if (protocol == null || state == null) return true;
            if (state.LastUpdateUtc == DateTime.MinValue) return true;

            double tgtRa, tgtDec;
            try
            {
                tgtRa  = CoordFormat.ParseHours(protocol.GetTargetRA());
                tgtDec = CoordFormat.ParseDegrees(protocol.GetTargetDec());
            }
            catch
            {
                return true; // Can't read target — fail-open.
            }

            double curRa  = state.RightAscension;
            double curDec = state.Declination;
            double dist = AngularSeparationDeg(curRa, curDec, tgtRa, tgtDec);
            if (dist <= limit) return true;

            string msg = string.Format(CultureInfo.InvariantCulture,
                "Sync would move the mount by {0:F2}° (configured sync limit: {1}°).\n\n" +
                "Current : RA {2}  Dec {3}\n" +
                "Target  : RA {4}  Dec {5}\n\n" +
                "A large delta usually means a bad plate-solve near the meridian or wrong site/time. " +
                "Continue with this sync?",
                dist, limit,
                CoordFormat.FormatHoursHighPrec(curRa), CoordFormat.FormatDegreesHighPrec(curDec),
                CoordFormat.FormatHoursHighPrec(tgtRa), CoordFormat.FormatDegreesHighPrec(tgtDec));
            try { TransportLogger.Note(string.Format(CultureInfo.InvariantCulture,
                "Sync limit exceeded: dist={0:F2}° limit={1}°", dist, limit)); } catch { }

            return PromptOnUiThread(msg);
        }

        private static bool PromptOnUiThread(string msg)
        {
            var hub = Program.Hub;
            try
            {
                if (hub != null && hub.IsHandleCreated && hub.InvokeRequired)
                {
                    return (bool)hub.Invoke(new Func<bool>(() => ShowDialog(hub, msg)));
                }
                return ShowDialog(hub, msg);
            }
            catch
            {
                return false; // UI failure: refuse rather than silently allow.
            }
        }

        private static bool ShowDialog(IWin32Window owner, string msg)
        {
            var res = MessageBox.Show(owner, msg,
                "OnStepX sync limit exceeded",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            return res == DialogResult.OK;
        }

        // Great-circle angular separation. RA in hours, Dec in degrees.
        private static double AngularSeparationDeg(double ra1Hours, double dec1Deg, double ra2Hours, double dec2Deg)
        {
            double ra1 = ra1Hours * Math.PI / 12.0;
            double ra2 = ra2Hours * Math.PI / 12.0;
            double d1 = dec1Deg * Math.PI / 180.0;
            double d2 = dec2Deg * Math.PI / 180.0;
            double cos = Math.Sin(d1) * Math.Sin(d2) + Math.Cos(d1) * Math.Cos(d2) * Math.Cos(ra1 - ra2);
            if (cos > 1) cos = 1; else if (cos < -1) cos = -1;
            return Math.Acos(cos) * 180.0 / Math.PI;
        }
    }
}
