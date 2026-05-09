using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Hardware.Transport;
using ASCOM.OnStepX.Services;

namespace ASCOM.OnStepX.ViewModels
{
    // Drives the title-bar update banner and the Advanced > Software Updates
    // section. Owns a single CheckLatest pass + an Install pass; both run on
    // background threads and marshal results through Dispatcher.BeginInvoke.
    public sealed class UpdateViewModel : ViewModelBase
    {
        public enum UpdateStatus { Idle, Checking, Available, UpToDate, Downloading, Installing, Error }

        private CancellationTokenSource _checkCts;
        private CancellationTokenSource _downloadCts;
        private UpdateService.UpdateInfo _info;

        public UpdateViewModel()
        {
            CheckCommand        = new RelayCommand(_ => DoCheck(),         _ => Status != UpdateStatus.Checking && Status != UpdateStatus.Downloading);
            OpenDialogCommand   = new RelayCommand(_ => OpenDialog(),      _ => IsAvailable);
            InstallCommand      = new RelayCommand(_ => DoInstall(),       _ => IsAvailable && Status != UpdateStatus.Downloading && Status != UpdateStatus.Installing);
            CancelDownloadCommand = new RelayCommand(_ => CancelDownload(), _ => Status == UpdateStatus.Downloading);
        }

        // ---- Bound state ----------------------------------------------------

        private UpdateStatus _status = UpdateStatus.Idle;
        public UpdateStatus Status
        {
            get => _status;
            private set
            {
                if (!Set(ref _status, value)) return;
                OnPropertyChanged(nameof(IsChecking));
                OnPropertyChanged(nameof(IsDownloading));
                CommandManager.InvalidateRequerySuggested();
            }
        }
        public bool IsChecking    => _status == UpdateStatus.Checking;
        public bool IsDownloading => _status == UpdateStatus.Downloading;

        private string _latestVersionText = "";
        public string LatestVersionText { get => _latestVersionText; private set => Set(ref _latestVersionText, value); }

        private string _statusText = "";
        public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

        private string _releaseNotes = "";
        public string ReleaseNotes { get => _releaseNotes; private set => Set(ref _releaseNotes, value); }

        private bool _isAvailable;
        public bool IsAvailable
        {
            get => _isAvailable;
            private set
            {
                if (!Set(ref _isAvailable, value)) return;
                OnPropertyChanged(nameof(BannerVisibility));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public Visibility BannerVisibility => _isAvailable ? Visibility.Visible : Visibility.Collapsed;

        private int _downloadProgress;
        public int DownloadProgress { get => _downloadProgress; private set => Set(ref _downloadProgress, value); }

        public string CurrentVersionText => "v" + UpdateService.Current.Major + "." + UpdateService.Current.Minor + "." + Math.Max(0, UpdateService.Current.Build);

        public bool CheckOnStartup
        {
            get => DriverSettings.CheckUpdatesOnStartup;
            set
            {
                if (DriverSettings.CheckUpdatesOnStartup == value) return;
                try { DriverSettings.CheckUpdatesOnStartup = value; } catch { }
                OnPropertyChanged();
            }
        }

        // ---- Commands -------------------------------------------------------

        public ICommand CheckCommand { get; }
        public ICommand OpenDialogCommand { get; }
        public ICommand InstallCommand { get; }
        public ICommand CancelDownloadCommand { get; }

        // Fire-and-forget startup hook. Same as CheckCommand without the user-facing
        // "Up to date" status — silent when nothing to surface.
        public void StartStartupCheck()
        {
            DoCheck(silentWhenUpToDate: true);
        }

        // ---- Check ----------------------------------------------------------

        private void DoCheck(bool silentWhenUpToDate = false)
        {
            try { _checkCts?.Cancel(); } catch { }
            _checkCts = new CancellationTokenSource();
            var ct = _checkCts.Token;

            Status = UpdateStatus.Checking;
            StatusText = "Checking for updates…";

            Task.Run(async () =>
            {
                UpdateService.UpdateInfo info = null;
                Exception err = null;
                try { info = await UpdateService.CheckLatestAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { err = ex; }

                BeginInvoke(() => OnCheckCompleted(info, err, silentWhenUpToDate));
            });
        }

        private void OnCheckCompleted(UpdateService.UpdateInfo info, Exception err, bool silentWhenUpToDate)
        {
            if (err != null)
            {
                Status = UpdateStatus.Error;
                StatusText = "Update check failed: " + err.Message;
                IsAvailable = false;
                return;
            }
            if (info == null)
            {
                Status = UpdateStatus.Error;
                StatusText = "Could not read latest release from GitHub.";
                IsAvailable = false;
                return;
            }

            _info = info;
            string vText = "v" + info.Latest.Major + "." + info.Latest.Minor + "." + Math.Max(0, info.Latest.Build);

            if (info.IsNewerThanCurrent)
            {
                LatestVersionText = "New Version Available " + vText;
                ReleaseNotes = info.Body ?? "";
                IsAvailable = true;
                Status = UpdateStatus.Available;
                StatusText = "Update available: " + vText;
                TransportLogger.Note("Update available: " + info.TagName + " (current " + CurrentVersionText + ")");
            }
            else
            {
                IsAvailable = false;
                Status = UpdateStatus.UpToDate;
                StatusText = silentWhenUpToDate ? "" : ("You're on the latest version (" + CurrentVersionText + ").");
            }
        }

        // ---- Dialog ---------------------------------------------------------

        private void OpenDialog()
        {
            try
            {
                var dlg = new Views.UpdateDialog(this) { Owner = Application.Current?.MainWindow };
                dlg.ShowDialog();
            }
            catch (Exception ex) { TransportLogger.Note("Update dialog open failed: " + ex.Message); }
        }

        // ---- Install --------------------------------------------------------

        private void DoInstall()
        {
            if (_info == null) return;

            // Asset missing: open release page in browser as a soft-fallback.
            if (string.IsNullOrEmpty(_info.AssetUrl))
            {
                StatusText = "Installer asset not found on this release. Opening release page…";
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_info.HtmlUrl) { UseShellExecute = true }); }
                catch (Exception ex) { TransportLogger.Note("Could not open release page: " + ex.Message); }
                return;
            }

            try { _downloadCts?.Cancel(); } catch { }
            _downloadCts = new CancellationTokenSource();
            var ct = _downloadCts.Token;

            DownloadProgress = 0;
            Status = UpdateStatus.Downloading;
            StatusText = "Downloading " + _info.AssetName + "…";

            string url = _info.AssetUrl;
            string assetName = _info.AssetName;

            Task.Run(async () =>
            {
                string path = null;
                Exception err = null;
                try
                {
                    path = await UpdateService.DownloadInstallerAsync(
                        url, assetName,
                        pct => BeginInvoke(() => DownloadProgress = pct),
                        ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { err = ex; }

                BeginInvoke(() => OnDownloadCompleted(path, err));
            });
        }

        private void OnDownloadCompleted(string path, Exception err)
        {
            if (err != null || string.IsNullOrEmpty(path))
            {
                Status = UpdateStatus.Error;
                StatusText = "Download failed" + (err == null ? "." : (": " + err.Message));
                return;
            }

            Status = UpdateStatus.Installing;
            StatusText = "Launching installer — hub will restart.";

            // Spawn the bridge .cmd, then exit so Inno's CloseApplications check
            // sees no live hub holding the install dir.
            bool spawned = UpdateService.LaunchInstallerAndScheduleRestart(path);
            if (!spawned)
            {
                Status = UpdateStatus.Error;
                StatusText = "Could not launch installer.";
                return;
            }
            try { Application.Current?.Shutdown(); } catch { }
        }

        private void CancelDownload()
        {
            try { _downloadCts?.Cancel(); } catch { }
            Status = UpdateStatus.Available;
            StatusText = "Download cancelled.";
            DownloadProgress = 0;
        }

        private static void BeginInvoke(Action a)
        {
            try { Application.Current?.Dispatcher.BeginInvoke(a); } catch { }
        }
    }
}
