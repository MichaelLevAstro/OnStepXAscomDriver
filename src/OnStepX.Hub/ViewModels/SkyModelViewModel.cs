using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ASCOM.OnStepX.Astronomy;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Diagnostics;
using ASCOM.OnStepX.Hardware;
using ASCOM.OnStepX.Hardware.State;

namespace ASCOM.OnStepX.ViewModels
{
    // Drives the Sky Model tab: reads the mount's alignment-model stars, plots them
    // on the dome, deletes one (read-modify-rewrite, since the firmware has no
    // per-point delete), clears the whole model, and exposes the :An#/:A+#/:AW#
    // helpers so a NINA "Solve & Sync" grid can build a model hands-free.
    //
    // The firmware read (:GX0x#) and write (:SX0x#) paths share one static star
    // index, so every read or rewrite must run as an atomic sequence. _alignLock
    // serialises them and IsBusy disables the buttons while one is running.
    public sealed class SkyModelViewModel : ViewModelBase
    {
        private readonly MainViewModel _main;
        private readonly MountSession _mount = MountSession.Instance;
        private readonly object _alignLock = new object();

        public ObservableCollection<SkyModelPoint> Points { get; } = new ObservableCollection<SkyModelPoint>();

        public SkyModelViewModel(MainViewModel main)
        {
            _main = main;
            Points.CollectionChanged += OnPointsChanged;

            RefreshCommand        = new RelayCommand(DoRefresh,        () => IsConnected && !IsBusy);
            DeleteSelectedCommand = new RelayCommand(DoDeleteSelected, () => IsConnected && !IsBusy && SelectedPoint != null);
            ClearAllCommand       = new RelayCommand(DoClearAll,       () => IsConnected && !IsBusy && Points.Count > 0);
            StartBuildCommand     = new RelayCommand(DoStartBuild,     () => IsConnected && !IsBusy && !AlignActive);
            AcceptPointCommand    = new RelayCommand(DoAcceptPoint,    () => IsConnected && !IsBusy && AlignActive);
            FinishBuildCommand    = new RelayCommand(DoFinishBuild,    () => IsConnected && !IsBusy && AlignActive);
        }

        public ICommand RefreshCommand { get; }
        public ICommand DeleteSelectedCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand StartBuildCommand { get; }
        public ICommand AcceptPointCommand { get; }
        public ICommand FinishBuildCommand { get; }

        public bool IsConnected => _main.State == ConnState.Connected;

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set { if (Set(ref _isBusy, value)) CommandManager.InvalidateRequerySuggested(); }
        }

        // Set true once :A?# reports a usable max-star count, i.e. the firmware was
        // built with ALIGN_MAX_NUM_STARS > 1. When false the tab shows a note.
        private bool _modelSupported = true;
        public bool ModelSupported { get => _modelSupported; private set => Set(ref _modelSupported, value); }

        private int _maxStars = 9;
        public int MaxStars
        {
            get => _maxStars;
            private set { if (Set(ref _maxStars, value)) OnPropertyChanged(nameof(BuildStarCount)); }
        }

        private bool _alignActive;
        public bool AlignActive
        {
            get => _alignActive;
            private set { if (Set(ref _alignActive, value)) CommandManager.InvalidateRequerySuggested(); }
        }

        private int _buildStarCount = 6;
        public int BuildStarCount
        {
            get => Math.Min(_buildStarCount, Math.Max(1, MaxStars));
            set => Set(ref _buildStarCount, Math.Max(1, Math.Min(9, value)));
        }

        public int PointCount => Points.Count;

        private string _statusText = "";
        public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

        private SkyModelPoint _selectedPoint;
        public SkyModelPoint SelectedPoint
        {
            get => _selectedPoint;
            set
            {
                if (ReferenceEquals(_selectedPoint, value)) return;
                if (_selectedPoint != null) _selectedPoint.IsSelected = false;
                _selectedPoint = value;
                if (_selectedPoint != null) _selectedPoint.IsSelected = true;
                OnPropertyChanged(nameof(SelectedPoint));
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectedInfoText));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool HasSelection => _selectedPoint != null;

        public string SelectedInfoText
        {
            get
            {
                var p = _selectedPoint;
                if (p == null) return "No point selected";
                return string.Format(CultureInfo.InvariantCulture,
                    "#{0}   Alt {1:F1}°  Az {2:F1}°   pier {3}   err {4}",
                    p.Number, p.AltDeg, p.AzDeg, p.PierText, p.ErrorText);
            }
        }

        // ---------- lifecycle ----------

        internal void OnConnStateChanged()
        {
            OnPropertyChanged(nameof(IsConnected));
            CommandManager.InvalidateRequerySuggested();
        }

        public void OnConnected()
        {
            DoRefresh();
        }

        public void OnDisconnected()
        {
            SelectedPoint = null;
            Points.Clear();
            AlignActive = false;
            StatusText = "Disconnected";
        }

        // ---------- commands ----------

        private void DoRefresh()
        {
            RunExclusive("Reading model…", () =>
            {
                var r = ReadModelLocked();
                OnUi(() => ApplyReadResult(r));
            });
        }

        private void DoDeleteSelected()
        {
            var sel = SelectedPoint;
            if (sel == null) return;

            // Snapshot the survivors from the points already in memory — the
            // firmware can't drop one point, so we rewrite the rest.
            var survivors = new List<SkyModelPoint>();
            foreach (var p in Points)
                if (!ReferenceEquals(p, sel)) survivors.Add(p);

            var confirm = MessageBox.Show(
                "Delete align point #" + sel.Number + "?\r\n\r\n" +
                "The mount has no per-point delete, so the model will be rebuilt from the " +
                "remaining " + survivors.Count + " point(s) and saved to the mount.",
                "Delete sky-model point", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            RunExclusive("Rewriting model…", () =>
            {
                ReuploadLocked(survivors);
                var r = ReadModelLocked();
                OnUi(() =>
                {
                    ApplyReadResult(r);
                    StatusText = "Deleted point. Model rebuilt with " + r.Points.Count + " point(s).";
                });
            });
        }

        private void DoClearAll()
        {
            var confirm = MessageBox.Show(
                "Clear the entire sky model (" + Points.Count + " point(s))?\r\n\r\n" +
                "This resets the alignment model on the mount and saves the cleared state. " +
                "It cannot be undone.",
                "Clear sky model", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            RunExclusive("Clearing model…", () =>
            {
                lock (_alignLock)
                {
                    if (!_mount.IsOpen) return;
                    _mount.Protocol.ResetAlignUpload();
                    _mount.Protocol.WriteModelToEeprom();
                }
                var r = ReadModelLocked();
                OnUi(() =>
                {
                    ApplyReadResult(r);
                    StatusText = "Model cleared.";
                });
            });
        }

        private void DoStartBuild()
        {
            int n = BuildStarCount;
            var confirm = MessageBox.Show(
                "Start a " + n + "-star alignment build?\r\n\r\n" +
                "The mount must be at the home position first — starting an align resets " +
                "home and turns tracking on. Then run a NINA 'Solve & Sync' grid (each sync " +
                "becomes a model point), or centre a star and press 'Accept point'. " +
                "Press 'Finish' to save.",
                "Start sky-model build", MessageBoxButton.OKCancel, MessageBoxImage.Information);
            if (confirm != MessageBoxResult.OK) return;

            RunExclusive("Starting align…", () =>
            {
                bool ok;
                lock (_alignLock)
                {
                    if (!_mount.IsOpen) return;
                    ok = _mount.Protocol.StartAlign(n);
                }
                var status = ReadStatusLocked();
                OnUi(() =>
                {
                    AlignActive = status.Active;
                    StatusText = ok
                        ? "Align started (" + status.CurrentStar + "/" + status.LastStar + "). Solve & Sync in NINA, or Accept points."
                        : "Mount rejected align start (check it is homed and tracking-capable).";
                });
            });
        }

        private void DoAcceptPoint()
        {
            RunExclusive("Accepting point…", () =>
            {
                lock (_alignLock)
                {
                    if (!_mount.IsOpen) return;
                    _mount.Protocol.AcceptAlignStar();
                }
                var status = ReadStatusLocked();
                OnUi(() =>
                {
                    AlignActive = status.Active;
                    StatusText = "Accepted (" + status.CurrentStar + "/" + status.LastStar + ").";
                });
            });
        }

        private void DoFinishBuild()
        {
            RunExclusive("Saving model…", () =>
            {
                lock (_alignLock)
                {
                    if (!_mount.IsOpen) return;
                    _mount.Protocol.WriteModelToEeprom();
                }
                var r = ReadModelLocked();
                OnUi(() =>
                {
                    AlignActive = false;
                    ApplyReadResult(r);
                    StatusText = "Model saved with " + r.Points.Count + " point(s).";
                });
            });
        }

        // ---------- mount sequences (run under _alignLock on a background thread) ----------

        private sealed class ReadResult
        {
            public List<SkyModelPoint> Points = new List<SkyModelPoint>();
            public bool Supported;
            public int MaxStars = 9;
        }

        private ReadResult ReadModelLocked()
        {
            var r = new ReadResult();
            lock (_alignLock)
            {
                if (!_mount.IsOpen) return r;

                AlignStatus status = _mount.Protocol.GetAlignStatus();
                r.Supported = status.Supported;
                r.MaxStars = status.Supported ? Math.Max(1, status.MaxStars) : 9;

                int n = _mount.Protocol.GetModelStarCount(); // also resets the read index
                if (n < 0) n = 0;
                if (n > 9) n = 9;

                double lat = DriverSettings.SiteLatitude;
                for (int i = 0; i < n; i++)
                {
                    double aHa = _mount.Protocol.GetModelActualHa();
                    double aDec = _mount.Protocol.GetModelActualDec();
                    double mHa = _mount.Protocol.GetModelMountHa();
                    double mDec = _mount.Protocol.GetModelMountDec();
                    int side = _mount.Protocol.GetModelStarPierSideAdvance(); // advances index

                    double alt = double.NaN, az = double.NaN;
                    if (!double.IsNaN(aHa) && !double.IsNaN(aDec))
                        AltAzTransform.ToAltAz(aHa, aDec, lat, out alt, out az);

                    double err = (!double.IsNaN(aHa) && !double.IsNaN(aDec) &&
                                  !double.IsNaN(mHa) && !double.IsNaN(mDec))
                        ? AltAzTransform.AngularSeparationArcsec(aHa, aDec, mHa, mDec)
                        : double.NaN;

                    r.Points.Add(new SkyModelPoint(i + 1, aHa, aDec, mHa, mDec, side, alt, az, err));
                }
            }
            return r;
        }

        private void ReuploadLocked(List<SkyModelPoint> stars)
        {
            lock (_alignLock)
            {
                if (!_mount.IsOpen) return;
                var p = _mount.Protocol;
                p.ResetAlignUpload();
                foreach (var s in stars)
                {
                    p.UploadActualHa(s.ActualHaHours);
                    p.UploadActualDec(s.ActualDecDeg);
                    p.UploadMountHa(s.MountHaHours);
                    p.UploadMountDec(s.MountDecDeg);
                    p.UploadStarPierSideAdvance(s.PierSide); // advances index
                }
                if (stars.Count >= 1) p.BuildModel();
                p.WriteModelToEeprom();
            }
        }

        private AlignStatus ReadStatusLocked()
        {
            lock (_alignLock)
            {
                if (!_mount.IsOpen) return default(AlignStatus);
                return _mount.Protocol.GetAlignStatus();
            }
        }

        // ---------- UI plumbing ----------

        private void ApplyReadResult(ReadResult r)
        {
            int keepNumber = _selectedPoint?.Number ?? -1;
            SelectedPoint = null;
            Points.Clear();
            foreach (var p in r.Points) Points.Add(p);

            ModelSupported = r.Supported;
            MaxStars = r.MaxStars;

            if (keepNumber >= 1)
                foreach (var p in Points)
                    if (p.Number == keepNumber) { SelectedPoint = p; break; }

            if (!r.Supported)
                StatusText = "This firmware does not expose the alignment model (ALIGN_MAX_NUM_STARS not enabled).";
            else if (r.Points.Count == 0)
                StatusText = "No model points. Build one with the controls below or a NINA Solve & Sync grid.";
            else
                StatusText = r.Points.Count + " point(s).";
        }

        private void RunExclusive(string busyStatus, Action work)
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusText = busyStatus;
            Task.Run(() =>
            {
                try
                {
                    work();
                }
                catch (Exception ex)
                {
                    DebugLogger.LogException("SkyModel", ex);
                    OnUi(() => StatusText = "Error: " + ex.Message);
                }
                finally
                {
                    OnUi(() => IsBusy = false);
                }
            });
        }

        private void OnPointsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(PointCount));
            CommandManager.InvalidateRequerySuggested();
        }

        private static void OnUi(Action a)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null || d.CheckAccess()) a();
            else d.BeginInvoke(a);
        }
    }
}
