using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Hardware;

namespace ASCOM.OnStepX.ViewModels
{
    // Site card. Mirrors HubForm.BuildSiteGroup + DoWriteSite + DoSyncLocationFromPc
    // + OpenSitesManager.
    public sealed class SiteViewModel : ViewModelBase
    {
        private readonly MainViewModel _main;
        private readonly MountSession _mount = MountSession.Instance;

        private string _latitude;
        private string _longitude;
        private string _elevation;

        public string Latitude  { get => _latitude;  set => Set(ref _latitude,  value); }
        public string Longitude { get => _longitude; set => Set(ref _longitude, value); }
        public string Elevation { get => _elevation; set => Set(ref _elevation, value); }

        private bool _syncFromPcBusy;
        public bool SyncFromPcBusy { get => _syncFromPcBusy; private set { if (Set(ref _syncFromPcBusy, value)) OnPropertyChanged(nameof(SyncFromPcText)); } }
        public string SyncFromPcText => _syncFromPcBusy ? "Locating..." : "Sync from PC location";

        // Mount-action controls disabled until connected; sites button locked
        // only during the transient Connecting state.
        public bool MountActionsEnabled    => _main.State == ConnState.Connected;
        public bool SitesButtonEnabled     => _main.State != ConnState.Connecting;

        public ICommand SyncFromPcCommand { get; }
        public ICommand UploadCommand { get; }
        public ICommand SitesCommand { get; }

        public SiteViewModel(MainViewModel main)
        {
            _main = main;
            SyncFromPcCommand = new RelayCommand(DoSyncLocationFromPc, () => MountActionsEnabled && !SyncFromPcBusy);
            UploadCommand     = new RelayCommand(DoWriteSite,          () => MountActionsEnabled);
            SitesCommand      = new RelayCommand(OpenSitesManager,     () => SitesButtonEnabled);
            LoadFromSettings();
        }

        internal void OnConnStateChanged()
        {
            OnPropertyChanged(nameof(MountActionsEnabled));
            OnPropertyChanged(nameof(SitesButtonEnabled));
            CommandManager.InvalidateRequerySuggested();
        }

        private void LoadFromSettings()
        {
            Latitude  = CoordFormat.FormatLatitudeDms(DriverSettings.SiteLatitude);
            Longitude = CoordFormat.FormatLongitudeDms(DriverSettings.SiteLongitude);
            Elevation = DriverSettings.SiteElevation.ToString("F1", CultureInfo.InvariantCulture);
        }

        // After the dialog applies a site, reflect it in the editor textboxes.
        internal void ApplyAppliedSite(Site s)
        {
            if (s == null) return;
            Latitude  = CoordFormat.FormatLatitudeDms(s.Latitude);
            Longitude = CoordFormat.FormatLongitudeDms(s.Longitude);
            Elevation = s.Elevation.ToString("F1", CultureInfo.InvariantCulture);
        }

        // Reflect mount readback (used by ReconcileSiteLocationOnConnect).
        public void ApplyFromMount(double lat, double lonEastPos, double ele)
        {
            Latitude = CoordFormat.FormatLatitudeDms(lat);
            Longitude = CoordFormat.FormatLongitudeDms(lonEastPos);
            Elevation = ele.ToString("F1", CultureInfo.InvariantCulture);
        }

        private void DoWriteSite()
        {
            if (_main.State != ConnState.Connected)
            {
                MessageBox.Show("Not connected.", "Write site", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            double lat, lon, ele;
            try
            {
                if (!CoordFormat.TryParseDegrees(Latitude, out lat))
                    throw new FormatException("Latitude '" + Latitude + "' is not a valid DMS value (e.g. +32°45'12\").");
                if (!CoordFormat.TryParseDegrees(Longitude, out lon))
                    throw new FormatException("Longitude '" + Longitude + "' is not a valid DMS value (e.g. -117°09'30\").");
                if (!double.TryParse(Elevation, NumberStyles.Float, CultureInfo.InvariantCulture, out ele))
                    throw new FormatException("Elevation '" + Elevation + "' is not a valid number (metres).");
            }
            catch (Exception ex)
            {
                Views.CopyableMessage.Show("Write site", ex.Message);
                return;
            }

            try
            {
                bool latOk = _mount.Protocol.SetLatitude(lat);
                bool lonOk = _mount.Protocol.SetLongitude(lon);
                bool eleOk = _mount.Protocol.SetElevation(ele);
                if (!latOk || !lonOk || !eleOk)
                {
                    string err = "";
                    try { err = _mount.Protocol.GetLastError(); } catch { }
                    Views.CopyableMessage.Show("Upload site",
                        "Mount rejected one or more site values.\r\n" +
                        "  Latitude:  " + (latOk ? "OK" : "REJECTED") + "\r\n" +
                        "  Longitude: " + (lonOk ? "OK" : "REJECTED") + "\r\n" +
                        "  Elevation: " + (eleOk ? "OK" : "REJECTED") +
                        (string.IsNullOrWhiteSpace(err) ? "" : "\r\n\r\nMount error: " + err));
                    return;
                }
                DriverSettings.SiteLatitude  = lat;
                DriverSettings.SiteLongitude = lon;
                DriverSettings.SiteElevation = ele;
                MessageBox.Show("Site uploaded to mount.", "Upload site", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Views.CopyableMessage.Show("Upload site", "Upload failed:\r\n\r\n" + ex.ToString());
            }
        }

        private async void DoSyncLocationFromPc()
        {
            SyncFromPcBusy = true;
            try
            {
                using (var watcher = new System.Device.Location.GeoCoordinateWatcher(System.Device.Location.GeoPositionAccuracy.High))
                {
                    watcher.MovementThreshold = 1.0;
                    bool started = await Task.Run(() => watcher.TryStart(false, TimeSpan.FromSeconds(15)));
                    if (!started)
                    {
                        MessageBox.Show(
                            "Windows Location service is unavailable or disabled.\r\n" +
                            "Enable it in Settings → Privacy & security → Location, and allow desktop apps.",
                            "Location unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    var c = watcher.Position.Location;
                    int waited = 0;
                    while ((c.IsUnknown || double.IsNaN(c.Latitude)) && waited < 10000)
                    {
                        await Task.Delay(250);
                        c = watcher.Position.Location;
                        waited += 250;
                    }
                    if (c.IsUnknown)
                    {
                        MessageBox.Show("Could not obtain a location fix within 10 seconds.",
                            "Location unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    Latitude = CoordFormat.FormatLatitudeDms(c.Latitude);
                    Longitude = CoordFormat.FormatLongitudeDms(c.Longitude);
                    double ele = double.IsNaN(c.Altitude) ? 0.0 : c.Altitude;
                    Elevation = ele.ToString("F1", CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex)
            {
                Views.CopyableMessage.Show("Location error", "Location lookup failed:\r\n\r\n" + ex.ToString());
            }
            finally
            {
                SyncFromPcBusy = false;
            }
        }

        private void OpenSitesManager()
        {
            var dlg = new Views.SitesWindow(_main, _main.State == ConnState.Connected)
            {
                Owner = Application.Current?.MainWindow
            };
            if (dlg.ShowDialog() == true && dlg.AppliedSite != null)
                ApplyAppliedSite(dlg.AppliedSite);
        }
    }
}
