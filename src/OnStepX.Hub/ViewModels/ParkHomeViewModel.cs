using System;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Hardware;

namespace ASCOM.OnStepX.ViewModels
{
    // Park / Home / Go To card. Mirrors HubForm.BuildParkHomeGroup +
    // ReportIfRejected + DoNvReset + Guard + slew pad commands.
    // Manual slew pad lives on this VM since the press/release commands
    // share the same connected/disconnected gating.
    public sealed class ParkHomeViewModel : ViewModelBase
    {
        private readonly MainViewModel _main;
        private readonly MountSession _mount = MountSession.Instance;

        public bool MountActionsEnabled => _main.State == ConnState.Connected;

        // Drives the "Slew to PA Position" button. Recomputed on connect-state
        // changes and on save so the button enables the moment a position lands.
        public bool HasSavedPaPosition
        {
            get
            {
                try { return !string.IsNullOrEmpty(DriverSettings.PaPositionMode); }
                catch { return false; }
            }
        }
        public bool SlewPaEnabled => MountActionsEnabled && HasSavedPaPosition;

        public ICommand ParkCommand { get; }
        public ICommand UnparkCommand { get; }
        public ICommand SetHomeCommand { get; }     // :hF# — set home reference to current axes
        public ICommand SearchHomeCommand { get; }  // :hC# — go to home position
        public ICommand SetParkHereCommand { get; }
        public ICommand SlewToTargetCommand { get; }
        public ICommand SavePaPositionCommand { get; }
        public ICommand SlewToPaPositionCommand { get; }
        public ICommand NvResetCommand { get; }
        public ICommand SlewRateInfoCommand { get; }
        public ICommand StopCommand { get; }        // SlewPad STOP -> AbortSlew

        public ParkHomeViewModel(MainViewModel main)
        {
            _main = main;
            ParkCommand        = new RelayCommand(() => Guard(() => ReportIfRejected("Park",   _mount.Protocol.Park()))  , () => MountActionsEnabled);
            UnparkCommand      = new RelayCommand(() => Guard(() => ReportIfRejected("Unpark", _mount.Protocol.Unpark())), () => MountActionsEnabled);
            SetHomeCommand     = new RelayCommand(() => Guard(() => _mount.Protocol.FindHome()),                          () => MountActionsEnabled);
            SearchHomeCommand  = new RelayCommand(() => Guard(() => _mount.Protocol.GoHome()),                            () => MountActionsEnabled);
            SetParkHereCommand = new RelayCommand(() => Guard(() => _mount.Protocol.SetParkHere()),                       () => MountActionsEnabled);
            SlewToTargetCommand= new RelayCommand(OpenSlewTarget,                                                          () => MountActionsEnabled);
            SavePaPositionCommand   = new RelayCommand(DoSavePaPosition,    () => MountActionsEnabled);
            SlewToPaPositionCommand = new RelayCommand(DoSlewToPaPosition,  () => SlewPaEnabled);
            NvResetCommand     = new RelayCommand(DoNvReset,                                                               () => MountActionsEnabled);
            SlewRateInfoCommand= new RelayCommand(DoSlewRateInfo,                                                          () => MountActionsEnabled);
            StopCommand        = new RelayCommand(() => Guard(() => _mount.Protocol.AbortSlew()),                         () => MountActionsEnabled);
        }

        internal void OnConnStateChanged()
        {
            OnPropertyChanged(nameof(MountActionsEnabled));
            OnPropertyChanged(nameof(SlewPaEnabled));
            CommandManager.InvalidateRequerySuggested();
        }

        // SlewPad press/release — direction is "N", "S", "E", "W", "NE", "NW", "SE", "SW".
        public void BeginSlew(string dir)
        {
            if (_main.State != ConnState.Connected || string.IsNullOrEmpty(dir)) return;
            double rate = _main.Tracking.SlewRate;
            _mount.Protocol.SetMoveAxisRateRA(rate);
            _mount.Protocol.SetMoveAxisRateDec(rate);
            if (dir.Contains("N")) _mount.Protocol.MoveNorth();
            if (dir.Contains("S")) _mount.Protocol.MoveSouth();
            if (dir.Contains("E")) _mount.Protocol.MoveEast();
            if (dir.Contains("W")) _mount.Protocol.MoveWest();
        }
        public void EndSlew(string dir)
        {
            if (_main.State != ConnState.Connected || string.IsNullOrEmpty(dir)) return;
            if (dir.Contains("N")) _mount.Protocol.StopNorth();
            if (dir.Contains("S")) _mount.Protocol.StopSouth();
            if (dir.Contains("E")) _mount.Protocol.StopEast();
            if (dir.Contains("W")) _mount.Protocol.StopWest();
        }

        // Capture the current physical position so we can replay it later.
        // EQ-mode mounts (GEM / Eq Fork) get HA + Dec + pier; Alt-Az mounts
        // get Alt + Az directly. HA stays constant for a static mount even
        // as the sky rotates — that's why we save HA, not RA.
        private void DoSavePaPosition()
        {
            if (_main.State != ConnState.Connected) return;
            try
            {
                int mountType = -1;
                try { mountType = _mount.Protocol.GetMountType(); } catch { }
                bool isAltAz = mountType == 3;

                if (isAltAz)
                {
                    double alt = CoordFormat.ParseDegrees(_mount.Protocol.GetAlt());
                    double az  = CoordFormat.ParseDegrees(_mount.Protocol.GetAz());
                    DriverSettings.PaPositionMode = "AZ";
                    DriverSettings.PaPositionAlt = alt;
                    DriverSettings.PaPositionAz  = az;
                    DriverSettings.PaPositionHa  = double.NaN;
                    DriverSettings.PaPositionDec = double.NaN;
                    DriverSettings.PaPositionPier = "";
                    MessageBox.Show(
                        "PA position saved (Alt-Az):\r\n  Alt " +
                        alt.ToString("F3", CultureInfo.InvariantCulture) + "°\r\n  Az  " +
                        az.ToString("F3", CultureInfo.InvariantCulture)  + "°",
                        "Save PA Position", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    double ra  = CoordFormat.ParseHours(_mount.Protocol.GetRA());
                    double dec = CoordFormat.ParseDegrees(_mount.Protocol.GetDec());
                    double lst = CoordFormat.ParseHours(_mount.Protocol.GetSiderealTime());
                    string pier = ExtractPier(_mount.Protocol.GetPierSide());
                    double ha = WrapHaHours(lst - ra);
                    DriverSettings.PaPositionMode = "EQ";
                    DriverSettings.PaPositionHa  = ha;
                    DriverSettings.PaPositionDec = dec;
                    DriverSettings.PaPositionPier = pier;
                    DriverSettings.PaPositionAlt = double.NaN;
                    DriverSettings.PaPositionAz  = double.NaN;
                    MessageBox.Show(
                        "PA position saved (Equatorial):\r\n  HA  " +
                        ha.ToString("F4", CultureInfo.InvariantCulture) + " h\r\n  Dec " +
                        dec.ToString("F3", CultureInfo.InvariantCulture) + "°\r\n  Pier " +
                        (string.IsNullOrEmpty(pier) ? "?" : pier),
                        "Save PA Position", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                OnPropertyChanged(nameof(HasSavedPaPosition));
                OnPropertyChanged(nameof(SlewPaEnabled));
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                Views.CopyableMessage.Show("Save PA Position", "Save failed:\r\n\r\n" + ex.ToString());
            }
        }

        // Replay the saved PA position. EQ: rebuild target RA from current LST
        // + saved HA, restore preferred pier, fire :MS#. AZ: feed saved Alt/Az
        // and fire :MA#. Pier preference is restored from registry afterwards
        // so the temporary override doesn't leak into normal slews.
        private void DoSlewToPaPosition()
        {
            if (_main.State != ConnState.Connected) return;
            string mode = (DriverSettings.PaPositionMode ?? "").Trim().ToUpperInvariant();
            if (mode != "EQ" && mode != "AZ")
            {
                MessageBox.Show("No PA position has been saved yet.\r\nPress \"Save PA Position\" first.",
                    "Slew to PA Position", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int mountType = -1;
            try { mountType = _mount.Protocol.GetMountType(); } catch { }
            bool mountIsAltAz = mountType == 3;
            bool savedIsAltAz = mode == "AZ";
            if (mountType >= 1 && mountIsAltAz != savedIsAltAz)
            {
                MessageBox.Show(
                    "Saved PA position uses " + (savedIsAltAz ? "Alt-Az" : "Equatorial") +
                    " coordinates, but the mount is currently in " +
                    (mountIsAltAz ? "Alt-Az" : "Equatorial") + " mode.\r\n\r\n" +
                    "Switch the mount mode (Setup → Mount Settings) or save a new PA position.",
                    "Slew to PA Position", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (savedIsAltAz)
                {
                    double alt = DriverSettings.PaPositionAlt;
                    double az  = DriverSettings.PaPositionAz;
                    if (double.IsNaN(alt) || double.IsNaN(az))
                    {
                        MessageBox.Show("Saved PA position is incomplete.", "Slew to PA Position",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (!_mount.Protocol.SetTargetAlt(alt) || !_mount.Protocol.SetTargetAz(az))
                    {
                        ReportIfRejected("Set PA target", false);
                        return;
                    }
                    int rc = _mount.Protocol.SlewToTargetAltAz();
                    if (rc != 0) ReportSlewFailure("Slew to PA Position", rc);
                }
                else
                {
                    double ha = DriverSettings.PaPositionHa;
                    double dec = DriverSettings.PaPositionDec;
                    if (double.IsNaN(ha) || double.IsNaN(dec))
                    {
                        MessageBox.Show("Saved PA position is incomplete.", "Slew to PA Position",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    double lst = CoordFormat.ParseHours(_mount.Protocol.GetSiderealTime());
                    double ra = WrapRaHours(lst - ha);
                    string savedPier = (DriverSettings.PaPositionPier ?? "").Trim().ToUpperInvariant();
                    if (savedPier == "E" || savedPier == "W")
                    {
                        try
                        {
                            _mount.Protocol.SetPreferredPierSide(savedPier == "E"
                                ? LX200Protocol.PreferredPier.East
                                : LX200Protocol.PreferredPier.West);
                        }
                        catch { }
                    }
                    if (!_mount.Protocol.SetTargetRA(ra) || !_mount.Protocol.SetTargetDec(dec))
                    {
                        ReportIfRejected("Set PA target", false);
                        RestorePreferredPierFromSettings();
                        return;
                    }
                    int rc = _mount.Protocol.SlewToTarget();
                    RestorePreferredPierFromSettings();
                    if (rc != 0) ReportSlewFailure("Slew to PA Position", rc);
                }
            }
            catch (Exception ex)
            {
                Views.CopyableMessage.Show("Slew to PA Position", "Slew failed:\r\n\r\n" + ex.ToString());
            }
        }

        private void RestorePreferredPierFromSettings()
        {
            try
            {
                var pref = (DriverSettings.PreferredPierSide ?? "B").Trim();
                LX200Protocol.PreferredPier p;
                switch (string.IsNullOrEmpty(pref) ? 'B' : char.ToUpperInvariant(pref[0]))
                {
                    case 'E': p = LX200Protocol.PreferredPier.East; break;
                    case 'W': p = LX200Protocol.PreferredPier.West; break;
                    case 'A': p = LX200Protocol.PreferredPier.Auto; break;
                    default:  p = LX200Protocol.PreferredPier.Best; break;
                }
                _mount.Protocol.SetPreferredPierSide(p);
            }
            catch { }
        }

        private static double WrapHaHours(double h)
        {
            h = ((h + 12.0) % 24.0 + 24.0) % 24.0 - 12.0;
            return h;
        }
        private static double WrapRaHours(double h)
        {
            return ((h % 24.0) + 24.0) % 24.0;
        }
        private static string ExtractPier(string reply)
        {
            if (string.IsNullOrEmpty(reply)) return "";
            foreach (var c in reply.TrimEnd('#'))
            {
                if (c == 'E' || c == 'e') return "E";
                if (c == 'W' || c == 'w') return "W";
            }
            return "";
        }

        private void ReportSlewFailure(string op, int rc)
        {
            string reason = rc switch
            {
                1 => "below horizon limit",
                2 => "above overhead limit",
                3 => "no object selected",
                4 => "position unreachable",
                5 => "another slew already in progress",
                6 => "outside limits",
                _ => "code " + rc.ToString(CultureInfo.InvariantCulture),
            };
            string err = "";
            try { err = _mount.Protocol.GetLastError(); } catch { }
            err = (err ?? "").TrimEnd('#').Trim();
            MessageBox.Show(
                op + " rejected by mount: " + reason +
                (string.IsNullOrEmpty(err) ? "" : "\r\nMount error: " + err),
                op, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void OpenSlewTarget()
        {
            var dlg = new Views.SlewTargetWindow(_main)
            {
                Owner = Application.Current?.MainWindow
            };
            dlg.ShowDialog();
        }

        private void DoNvReset()
        {
            if (_main.State != ConnState.Connected) return;
            var r = MessageBox.Show(
                "This will WIPE the mount's non-volatile memory to factory defaults.\r\n\r\n" +
                "All saved configuration on the mount (axis settings, park position, " +
                "limits, site, time, slew rates) will be lost.\r\n\r\n" +
                "The mount will reboot and the driver will disconnect.\r\n\r\n" +
                "Continue?",
                "NV Reset — Destructive",
                MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (r != MessageBoxResult.Yes) return;

            try
            {
                _mount.Protocol.ResetNvMemory();
                Thread.Sleep(250);
                _mount.Protocol.RebootMount();
            }
            catch (Exception ex)
            {
                Views.CopyableMessage.Show("NV Reset", "Send failed:\r\n\r\n" + ex.ToString());
            }
            _main.Connection.DoDisconnect();
            MessageBox.Show(
                "NV reset and reboot sent. Mount is restarting.\r\n" +
                "Wait ~10 seconds, then reconnect.",
                "NV Reset", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DoSlewRateInfo()
        {
            if (_main.State != ConnState.Connected) return;
            try
            {
                Views.CopyableMessage.Show("Slew Rate Diagnostics", FormatSlewRateProbe());
            }
            catch (Exception ex)
            {
                Views.CopyableMessage.Show("Slew Rate Diagnostics", "Probe failed:\r\n\r\n" + ex.ToString());
            }
        }

        private string FormatSlewRateProbe()
        {
            double usCur  = _mount.Protocol.GetUsPerStepCurrent();
            double usBase = _mount.Protocol.GetUsPerStepBase();
            double curDps = _mount.Protocol.GetCurrentStepRateDegPerSec();
            double usLim  = _mount.Protocol.GetUsPerStepLowerLimit();
            double baseDps = _mount.Protocol.GetBaseSlewRateDegPerSec();
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                ":GX92#={0:0.000} us/step cur ; :GX93#={1:0.000} us/step base ; :GX97#={2:0.###} deg/s cur ; :GX99#={3:0.000} us/step limit ; derived base={4:0.###} deg/s",
                usCur, usBase, curDps, usLim, baseDps);
        }

        private void Guard(Action a)
        {
            try { if (_main.State == ConnState.Connected) a(); }
            catch (Exception ex) { Views.CopyableMessage.Show("Error", ex.ToString()); }
        }

        private void ReportIfRejected(string op, bool ok)
        {
            if (ok) return;
            string err = "";
            try { err = _mount.Protocol.GetLastError(); } catch { }
            err = (err ?? "").TrimEnd('#').Trim();
            MessageBox.Show(
                op + " rejected by mount." +
                (string.IsNullOrEmpty(err) ? "" : "\r\nMount error: " + err) +
                "\r\n\r\nCheck that date/time and site location are set, and that a park\r\n" +
                "position has been defined (Park → then Unpark).",
                op, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
