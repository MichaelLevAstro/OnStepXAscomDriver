using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Hardware;
using Microsoft.Win32;

namespace ASCOM.OnStepX.ViewModels
{
    // Mirrors SitesManagerForm. PC-local site list, Apply pushes selected site
    // to mount via LX200 set-lat/lon/elev and updates DriverSettings so the
    // hub's reconcile-on-connect logic stays consistent.
    public sealed class SitesWindowViewModel : ViewModelBase
    {
        private readonly MainViewModel _main;
        private readonly MountSession _mount = MountSession.Instance;
        private readonly bool _connected;
        private List<Site> _sites;

        public ObservableCollection<SiteRow> Rows { get; } = new ObservableCollection<SiteRow>();

        private SiteRow _selectedRow;
        public SiteRow SelectedRow
        {
            get => _selectedRow;
            set { if (Set(ref _selectedRow, value)) PopulateFieldsFromSelection(); }
        }

        private string _name = "";
        private string _latitude = "";
        private string _longitude = "";
        private string _elevation = "";
        public string Name      { get => _name;      set => Set(ref _name,      value); }
        public string Latitude  { get => _latitude;  set => Set(ref _latitude,  value); }
        public string Longitude { get => _longitude; set => Set(ref _longitude, value); }
        public string Elevation { get => _elevation; set => Set(ref _elevation, value); }

        private string _status = "";
        public string Status { get => _status; private set => Set(ref _status, value); }

        public bool ApplyEnabled => _connected;
        public string ApplyText => _connected ? "Apply to mount" : "Apply (not connected)";

        internal Site AppliedSite { get; private set; }
        public Action<bool> CloseAction { get; set; }

        public ICommand AddCommand { get; }
        public ICommand UpdateCommand { get; }
        public ICommand RemoveCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand LoadFromCurrentCommand { get; }
        public ICommand ApplyCommand { get; }
        public ICommand CloseCommand { get; }

        public SitesWindowViewModel(MainViewModel main, bool connected)
        {
            _main = main;
            _connected = connected;
            _sites = SiteStore.Load();

            AddCommand              = new RelayCommand(OnAdd);
            UpdateCommand           = new RelayCommand(OnUpdate);
            RemoveCommand           = new RelayCommand(OnRemove);
            ImportCommand           = new RelayCommand(OnImport);
            ExportCommand           = new RelayCommand(OnExport);
            LoadFromCurrentCommand  = new RelayCommand(OnLoadFromCurrent);
            ApplyCommand            = new RelayCommand(OnApply, () => ApplyEnabled);
            CloseCommand            = new RelayCommand(() => CloseAction?.Invoke(false));

            RefreshList();
        }

        private void RefreshList()
        {
            Rows.Clear();
            foreach (var s in _sites.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
                Rows.Add(new SiteRow(s) { IsCurrent = IsCurrentSite(s) });
        }

        private static bool IsCurrentSite(Site s)
        {
            const double ll = 1.0 / 60.0;
            const double em = 10.0;
            return Math.Abs(s.Latitude - DriverSettings.SiteLatitude) < ll
                && Math.Abs(s.Longitude - DriverSettings.SiteLongitude) < ll
                && Math.Abs(s.Elevation - DriverSettings.SiteElevation) < em;
        }

        private void PopulateFieldsFromSelection()
        {
            if (_selectedRow?.Site == null) return;
            var s = _selectedRow.Site;
            Name = s.Name ?? "";
            Latitude = CoordFormat.FormatLatitudeDms(s.Latitude);
            Longitude = CoordFormat.FormatLongitudeDms(s.Longitude);
            Elevation = s.Elevation.ToString("F1", CultureInfo.InvariantCulture);
        }

        private bool ParseFields(out Site site, out string error)
        {
            site = null;
            error = null;
            var name = (Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name)) { error = "Name is required."; return false; }
            if (!CoordFormat.TryParseDegrees(Latitude, out var lat)) { error = "Invalid latitude."; return false; }
            if (!CoordFormat.TryParseDegrees(Longitude, out var lon)) { error = "Invalid longitude."; return false; }
            if (!double.TryParse(Elevation, NumberStyles.Float, CultureInfo.InvariantCulture, out var ele))
            { error = "Invalid elevation (metres)."; return false; }
            site = new Site { Name = name, Latitude = lat, Longitude = lon, Elevation = ele };
            return true;
        }

        private void OnLoadFromCurrent()
        {
            Latitude = CoordFormat.FormatLatitudeDms(DriverSettings.SiteLatitude);
            Longitude = CoordFormat.FormatLongitudeDms(DriverSettings.SiteLongitude);
            Elevation = DriverSettings.SiteElevation.ToString("F1", CultureInfo.InvariantCulture);
            Status = "Loaded current site into fields — enter a name and Add.";
        }

        private void OnAdd()
        {
            if (!ParseFields(out var site, out var err)) { Status = err; return; }
            if (_sites.Any(s => string.Equals(s.Name, site.Name, StringComparison.OrdinalIgnoreCase)))
            { Status = "A site named '" + site.Name + "' already exists — use Update."; return; }
            _sites.Add(site);
            SiteStore.Save(_sites);
            RefreshList();
            Status = "Added '" + site.Name + "'.";
        }

        private void OnUpdate()
        {
            if (!ParseFields(out var site, out var err)) { Status = err; return; }
            var existing = _sites.FirstOrDefault(s => string.Equals(s.Name, site.Name, StringComparison.OrdinalIgnoreCase));
            if (existing == null) { Status = "No site named '" + site.Name + "' — use Add."; return; }
            existing.Latitude = site.Latitude;
            existing.Longitude = site.Longitude;
            existing.Elevation = site.Elevation;
            SiteStore.Save(_sites);
            RefreshList();
            Status = "Updated '" + site.Name + "'.";
        }

        private void OnRemove()
        {
            if (_selectedRow?.Site == null) { Status = "Select a site to remove."; return; }
            var s = _selectedRow.Site;
            if (MessageBox.Show("Remove site '" + s.Name + "'?", "Confirm",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            _sites.RemoveAll(x => string.Equals(x.Name, s.Name, StringComparison.OrdinalIgnoreCase));
            SiteStore.Save(_sites);
            RefreshList();
            Status = "Removed '" + s.Name + "'.";
        }

        private void OnImport()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "OnStepX sites (*.json)|*.json|All files (*.*)|*.*",
                Title = "Import sites",
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                _sites = SiteStore.ImportFrom(dlg.FileName, _sites);
                SiteStore.Save(_sites);
                RefreshList();
                Status = "Imported from " + Path.GetFileName(dlg.FileName) + " (merged by name).";
            }
            catch (Exception ex)
            {
                Status = "Import failed: " + ex.Message;
            }
        }

        private void OnExport()
        {
            var dlg = new SaveFileDialog
            {
                Filter = "OnStepX sites (*.json)|*.json",
                Title = "Export sites",
                FileName = "sites.json",
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                SiteStore.ExportTo(dlg.FileName, _sites);
                Status = "Exported " + _sites.Count + " site(s) to " + Path.GetFileName(dlg.FileName) + ".";
            }
            catch (Exception ex)
            {
                Status = "Export failed: " + ex.Message;
            }
        }

        private void OnApply()
        {
            if (_selectedRow?.Site == null) { Status = "Select a site to apply."; return; }
            var s = _selectedRow.Site;
            var msg = string.Format(CultureInfo.InvariantCulture,
                "Write '{0}' to the mount?\n\nLatitude  {1}\nLongitude {2}\nElevation {3:F1} m",
                s.Name,
                CoordFormat.FormatLatitudeDms(s.Latitude),
                CoordFormat.FormatLongitudeDms(s.Longitude),
                s.Elevation);
            if (MessageBox.Show(msg, "Confirm site write",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

            try
            {
                bool latOk = _mount.Protocol.SetLatitude(s.Latitude);
                bool lonOk = _mount.Protocol.SetLongitude(s.Longitude);
                bool eleOk = _mount.Protocol.SetElevation(s.Elevation);
                if (!(latOk && lonOk && eleOk))
                {
                    Status = "Mount rejected one or more fields (lat=" + latOk + " lon=" + lonOk + " ele=" + eleOk + ").";
                    return;
                }
                DriverSettings.SiteLatitude = s.Latitude;
                DriverSettings.SiteLongitude = s.Longitude;
                DriverSettings.SiteElevation = s.Elevation;
                AppliedSite = s;
                Status = "Applied '" + s.Name + "' to mount.";
                CloseAction?.Invoke(true);
            }
            catch (Exception ex)
            {
                Status = "Apply failed: " + ex.Message;
            }
        }
    }

    public sealed class SiteRow : ViewModelBase
    {
        internal Site Site { get; }
        public string Name => Site?.Name ?? "";
        public string LatitudeText => CoordFormat.FormatLatitudeDms(Site.Latitude);
        public string LongitudeText => CoordFormat.FormatLongitudeDms(Site.Longitude);
        public string ElevationText => Site.Elevation.ToString("F1", CultureInfo.InvariantCulture);

        private bool _isCurrent;
        public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }

        internal SiteRow(Site s) { Site = s; }
    }
}
