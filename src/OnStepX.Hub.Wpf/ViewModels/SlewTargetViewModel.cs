using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using ASCOM.OnStepX.Astronomy;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Hardware;

namespace ASCOM.OnStepX.ViewModels
{
    // Mirrors SlewTargetForm. Catalog combo + filter + virtualized row list +
    // 2 s solar-system live refresh. Slew confirmation auto-switches tracking
    // rate for Sun/Moon/planets when DriverSettings.AutoSwitchPlanetTrackingRate.
    public sealed class SlewTargetViewModel : ViewModelBase
    {
        private readonly MainViewModel _main;
        private readonly MountSession _mount = MountSession.Instance;
        private IReadOnlyList<CelestialTarget> _currentCatalog = new CelestialTarget[0];
        private DispatcherTimer _liveTimer;
        private const int LiveTickMs = 2000;
        private const int MaxDisplayRows = 3000;

        public ObservableCollection<string> Catalogs { get; } = new ObservableCollection<string>
        { "Planets", "Messier", "NGC", "IC", "SH2", "LDN" };

        public ObservableCollection<TargetRow> Rows { get; } = new ObservableCollection<TargetRow>();
        public ICollectionView RowsView { get; }

        private string _selectedCatalog = "Planets";
        public string SelectedCatalog
        {
            get => _selectedCatalog;
            set { if (Set(ref _selectedCatalog, value)) RefreshList(); }
        }

        private string _filter = "";
        public string Filter
        {
            get => _filter;
            set { if (Set(ref _filter, value ?? "")) RowsView.Refresh(); }
        }

        private TargetRow _selectedRow;
        public TargetRow SelectedRow
        {
            get => _selectedRow;
            set { if (Set(ref _selectedRow, value)) UpdateSelection(); }
        }

        private string _selectedLabel = "No target selected";
        public string SelectedLabel { get => _selectedLabel; private set => Set(ref _selectedLabel, value); }

        private string _coordsLabel = "";
        public string CoordsLabel { get => _coordsLabel; private set => Set(ref _coordsLabel, value); }

        private string _statusText = "";
        public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

        public bool MountConnected => _main.State == ConnState.Connected;
        public bool SlewEnabled => MountConnected && SelectedRow != null;

        public ICommand RefreshCommand { get; }
        public ICommand SlewCommand { get; }
        public ICommand CloseCommand { get; }
        public Action CloseAction { get; set; }

        public SlewTargetViewModel(MainViewModel main)
        {
            _main = main;
            RowsView = CollectionViewSource.GetDefaultView(Rows);
            RowsView.Filter = MatchesFilter;

            RefreshCommand = new RelayCommand(RefreshList);
            SlewCommand    = new RelayCommand(DoSlew, () => SlewEnabled);
            CloseCommand   = new RelayCommand(() => CloseAction?.Invoke());

            _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(LiveTickMs) };
            _liveTimer.Tick += (s, e) => OnLiveTick();

            RefreshList();
        }

        public void Detach()
        {
            try { _liveTimer?.Stop(); } catch { }
        }

        private bool MatchesFilter(object o)
        {
            if (string.IsNullOrEmpty(_filter)) return true;
            var r = (TargetRow)o;
            string f = _filter;
            return r.Id.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                || r.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                || (r.Type ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RefreshList()
        {
            try
            {
                switch (_selectedCatalog)
                {
                    case "Planets": _currentCatalog = PlanetCatalog.All; break;
                    case "Messier": _currentCatalog = MessierCatalog.All; break;
                    case "NGC":     _currentCatalog = DeepSkyCatalog.Load("ngc.txt"); break;
                    case "IC":      _currentCatalog = DeepSkyCatalog.Load("ic.txt"); break;
                    case "SH2":     _currentCatalog = DeepSkyCatalog.Load("sh2.txt"); break;
                    case "LDN":     _currentCatalog = DeepSkyCatalog.Load("ldn.txt"); break;
                    default:        _currentCatalog = new CelestialTarget[0]; break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Catalog load failed: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                _currentCatalog = new CelestialTarget[0];
            }

            var utc = MountTime.NowUtc(_mount);
            Rows.Clear();
            int shown = 0;
            foreach (var t in _currentCatalog)
            {
                if (shown >= MaxDisplayRows) break;
                var (ra, dec) = t.Coords(utc);
                Rows.Add(new TargetRow(t)
                {
                    RA = FormatRaShort(ra),
                    Dec = FormatDecShort(dec),
                });
                shown++;
            }
            StatusText = _currentCatalog.Count > shown
                ? string.Format("Showing {0} of {1} entries — refine filter to narrow.", shown, _currentCatalog.Count)
                : string.Format("{0} entries", _currentCatalog.Count);

            bool hasPlanets = _currentCatalog.Any(t => t.SolarSystemBody.HasValue);
            if (hasPlanets) _liveTimer?.Start(); else _liveTimer?.Stop();
            UpdateSelection();
        }

        private void UpdateSelection()
        {
            OnPropertyChanged(nameof(SlewEnabled));
            CommandManager.InvalidateRequerySuggested();
            if (_selectedRow == null)
            {
                SelectedLabel = string.IsNullOrEmpty(_statusText) ? "No target selected" : _statusText;
                CoordsLabel = "";
                return;
            }
            var t = _selectedRow.Target;
            var (ra, dec) = t.Coords(MountTime.NowUtc(_mount));
            SelectedLabel = t.Name + (string.IsNullOrEmpty(t.Constellation) ? "" : " · " + t.Constellation);
            CoordsLabel = string.Format(CultureInfo.InvariantCulture,
                "RA {0}  Dec {1}  ({2})",
                CoordFormat.FormatHoursHighPrec(ra),
                CoordFormat.FormatDegreesHighPrec(dec),
                t.Kind);
        }

        private void DoSlew()
        {
            if (_selectedRow == null) return;
            if (_main.State != ConnState.Connected)
            {
                MessageBox.Show("Mount not connected.", "Slew", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var t = _selectedRow.Target;
            var (ra, dec) = t.Coords(MountTime.NowUtc(_mount));

            string rateName = null;
            bool willSwitchRate = t.SolarSystemBody.HasValue && DriverSettings.AutoSwitchPlanetTrackingRate;
            if (willSwitchRate) rateName = TrackingRateNameFor(t.SolarSystemBody.Value);

            string confirmText = string.Format(CultureInfo.InvariantCulture,
                "Slew mount to {0}?\r\nRA {1}\r\nDec {2}",
                t.Name, CoordFormat.FormatHoursHighPrec(ra), CoordFormat.FormatDegreesHighPrec(dec));
            if (willSwitchRate) confirmText += "\r\n\r\nTracking rate will switch to " + rateName + ".";

            if (MessageBox.Show(confirmText, "Confirm slew",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

            try
            {
                if (!_mount.Protocol.SetTargetRA(ra))   throw new Exception("Mount rejected RA");
                if (!_mount.Protocol.SetTargetDec(dec)) throw new Exception("Mount rejected Dec");
                int rc = _mount.Protocol.SlewToTarget();
                if (rc != 0)
                {
                    string msg;
                    switch (rc)
                    {
                        case 1: msg = "Below horizon"; break;
                        case 2: msg = "Above overhead limit"; break;
                        case 6: msg = "Outside meridian limits"; break;
                        default: msg = "Slew rejected (code " + rc + ")"; break;
                    }
                    if (rc == 1 || rc == 2 || rc == 6) _mount.RaiseLimitWarning(msg);
                    MessageBox.Show(msg, "Slew error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (willSwitchRate) TryApplyTrackingRate(t.SolarSystemBody.Value);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Slew failed: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string TrackingRateNameFor(Body b)
        {
            if (b == Body.Sun)  return "Solar";
            if (b == Body.Moon) return "Lunar";
            return "Sidereal";
        }

        private void TryApplyTrackingRate(Body b)
        {
            try
            {
                if      (b == Body.Sun)  _mount.Protocol.SetTrackingSolar();
                else if (b == Body.Moon) _mount.Protocol.SetTrackingLunar();
                else                     _mount.Protocol.SetTrackingSidereal();
            }
            catch { /* poll will fix UI within ~3 s */ }
        }

        private void OnLiveTick()
        {
            if (_currentCatalog == null || Rows.Count == 0) return;
            var utc = MountTime.NowUtc(_mount);
            foreach (var row in Rows)
            {
                var t = row.Target;
                if (t == null || !t.SolarSystemBody.HasValue) continue;
                var (ra, dec) = t.Coords(utc);
                row.RA = FormatRaShort(ra);
                row.Dec = FormatDecShort(dec);
            }
            if (_selectedRow != null && _selectedRow.Target.SolarSystemBody.HasValue)
            {
                var (ra, dec) = _selectedRow.Target.Coords(utc);
                CoordsLabel = string.Format(CultureInfo.InvariantCulture,
                    "RA {0}  Dec {1}  ({2})",
                    CoordFormat.FormatHoursHighPrec(ra),
                    CoordFormat.FormatDegreesHighPrec(dec),
                    _selectedRow.Target.Kind);
            }
        }

        public void OnConnStateChanged()
        {
            OnPropertyChanged(nameof(MountConnected));
            OnPropertyChanged(nameof(SlewEnabled));
            CommandManager.InvalidateRequerySuggested();
        }

        private static string FormatRaShort(double hours)
        {
            hours = ((hours % 24) + 24) % 24;
            int h = (int)hours;
            double rem = (hours - h) * 60.0;
            int m = (int)Math.Round(rem);
            if (m == 60) { m = 0; h = (h + 1) % 24; }
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", h, m);
        }

        private static string FormatDecShort(double deg)
        {
            char sign = deg < 0 ? '-' : '+';
            deg = Math.Abs(deg);
            int d = (int)deg;
            double rem = (deg - d) * 60.0;
            int m = (int)Math.Round(rem);
            if (m == 60) { m = 0; d++; }
            return string.Format(CultureInfo.InvariantCulture, "{0}{1:00}:{2:00}", sign, d, m);
        }
    }

    public sealed class TargetRow : ViewModelBase
    {
        internal CelestialTarget Target { get; }
        public string Id => Target?.Id ?? "";
        public string Name => Target?.Name ?? "";
        public string Type => string.IsNullOrEmpty(Target?.Constellation)
            ? Target?.Kind ?? ""
            : Target.Kind + " (" + Target.Constellation + ")";

        private string _ra;
        public string RA { get => _ra; set => Set(ref _ra, value); }

        private string _dec;
        public string Dec { get => _dec; set => Set(ref _dec, value); }

        internal TargetRow(CelestialTarget t) { Target = t; }
    }
}
