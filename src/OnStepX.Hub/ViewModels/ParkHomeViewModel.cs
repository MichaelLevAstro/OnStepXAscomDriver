using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;
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

        public ICommand ParkCommand { get; }
        public ICommand UnparkCommand { get; }
        public ICommand SetHomeCommand { get; }     // :hF# — set home reference to current axes
        public ICommand SearchHomeCommand { get; }  // :hC# — go to home position
        public ICommand SetParkHereCommand { get; }
        public ICommand SlewToTargetCommand { get; }
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
            NvResetCommand     = new RelayCommand(DoNvReset,                                                               () => MountActionsEnabled);
            SlewRateInfoCommand= new RelayCommand(DoSlewRateInfo,                                                          () => MountActionsEnabled);
            StopCommand        = new RelayCommand(() => Guard(() => _mount.Protocol.AbortSlew()),                         () => MountActionsEnabled);
        }

        internal void OnConnStateChanged()
        {
            OnPropertyChanged(nameof(MountActionsEnabled));
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
