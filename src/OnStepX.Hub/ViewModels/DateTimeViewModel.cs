using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Hardware;

namespace ASCOM.OnStepX.ViewModels
{
    // Date/Time card. Mirrors HubForm.BuildTimeGroup + DoSyncTime + DoWriteDateTime
    // + TickLocalTime. Date and time are exposed as plain string properties
    // (dd/MM/yyyy and HH:mm:ss) and rendered with TextBox — the WPF DatePicker
    // pulls in unstyled chrome and a system-locale calendar (Hebrew on RTL
    // machines), neither of which fits the dark theme. Manual editing is rare
    // because the typical flow is "Sync from PC" anyway.
    public sealed class DateTimeViewModel : ViewModelBase
    {
        private readonly MainViewModel _main;
        private readonly MountSession _mount = MountSession.Instance;

        // Display format kept invariant so manual edits round-trip the same way
        // regardless of OS locale (Hebrew, RTL, dotted dates etc.).
        private const string DateFormat = "dd/MM/yyyy";
        private const string TimeFormat = "HH:mm:ss";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private string _localDateText;
        private string _localTimeText;
        private double _utcOffsetHours;
        private bool _autoSyncOnConnect;

        public string LocalDateText { get => _localDateText; set => Set(ref _localDateText, value); }
        public string LocalTimeText { get => _localTimeText; set => Set(ref _localTimeText, value); }
        public double UtcOffsetHours { get => _utcOffsetHours; set => Set(ref _utcOffsetHours, Math.Max(-14, Math.Min(14, value))); }

        public bool AutoSyncOnConnect
        {
            get => _autoSyncOnConnect;
            set { if (Set(ref _autoSyncOnConnect, value)) { try { DriverSettings.AutoSyncTimeOnConnect = value; } catch { } } }
        }

        public bool MountActionsEnabled => _main.State == ConnState.Connected;

        public ICommand SyncFromPcCommand { get; }
        public ICommand UploadCommand { get; }

        public DateTimeViewModel(MainViewModel main)
        {
            _main = main;
            SyncFromPcCommand = new RelayCommand(DoSyncTime,      () => MountActionsEnabled);
            UploadCommand     = new RelayCommand(DoWriteDateTime, () => MountActionsEnabled);
            _autoSyncOnConnect = DriverSettings.AutoSyncTimeOnConnect;
            var now = DateTime.Now;
            _localDateText  = now.ToString(DateFormat, Inv);
            _localTimeText  = now.ToString(TimeFormat, Inv);
            _utcOffsetHours = Math.Round((now - now.ToUniversalTime()).TotalHours, 1);
        }

        internal void OnConnStateChanged()
        {
            OnPropertyChanged(nameof(MountActionsEnabled));
            CommandManager.InvalidateRequerySuggested();
        }

        // 250 ms tick — rolls the displayed time forward unless the user is
        // actively editing one of the fields. The "focused" guard mirrors
        // HubForm.TickLocalTime semantics so the user's keystrokes don't get
        // overwritten while they're typing a target time.
        public void Tick(bool dateFieldFocused, bool timeFieldFocused)
        {
            if (dateFieldFocused || timeFieldFocused) return;
            var now = DateTime.Now;
            string newDate = now.ToString(DateFormat, Inv);
            string newTime = now.ToString(TimeFormat, Inv);
            if (LocalDateText != newDate) LocalDateText = newDate;
            if (LocalTimeText != newTime) LocalTimeText = newTime;
        }

        // Combine date + time fields into one DateTime; falls back to "now" on
        // parse failure so the upload path doesn't silently send 0001-01-01.
        private DateTime ParseLocalOrNow()
        {
            DateTime date;
            if (!DateTime.TryParseExact(LocalDateText, DateFormat, Inv, DateTimeStyles.None, out date))
                date = DateTime.Now.Date;
            DateTime time;
            if (!DateTime.TryParseExact(LocalTimeText, TimeFormat, Inv, DateTimeStyles.None, out time))
                time = DateTime.Now;
            return date.Date.AddHours(time.Hour).AddMinutes(time.Minute).AddSeconds(time.Second);
        }

        public void DoSyncTime()
        {
            if (_main.State != ConnState.Connected) return;
            var now = DateTime.Now;
            double offsetH = (now - now.ToUniversalTime()).TotalHours;
            _mount.Protocol.SetUtcOffset(offsetH);
            _mount.Protocol.SetLocalDate(now);
            _mount.Protocol.SetLocalTime(now);
            UtcOffsetHours = Math.Round(offsetH, 1);
            LocalDateText = now.ToString(DateFormat, Inv);
            LocalTimeText = now.ToString(TimeFormat, Inv);
        }

        // Push the date/time/timezone currently shown in the UI to the mount.
        // Order matters: offset first so the subsequent LocalDate/LocalTime are
        // interpreted in the intended timezone (matches HubForm.DoWriteDateTime).
        private void DoWriteDateTime()
        {
            if (_main.State != ConnState.Connected) return;
            double offsetH = UtcOffsetHours;
            var local = ParseLocalOrNow();
            try
            {
                bool offOk  = _mount.Protocol.SetUtcOffset(offsetH);
                bool dateOk = _mount.Protocol.SetLocalDate(local);
                bool timeOk = _mount.Protocol.SetLocalTime(local);
                if (!offOk || !dateOk || !timeOk)
                {
                    string err = "";
                    try { err = _mount.Protocol.GetLastError(); } catch { }
                    Views.CopyableMessage.Show("Upload date/time",
                        "Mount rejected one or more values.\r\n" +
                        "  UTC offset: " + (offOk  ? "OK" : "REJECTED") + "\r\n" +
                        "  Date:       " + (dateOk ? "OK" : "REJECTED") + "\r\n" +
                        "  Time:       " + (timeOk ? "OK" : "REJECTED") +
                        (string.IsNullOrWhiteSpace(err) ? "" : "\r\n\r\nMount error: " + err));
                    return;
                }
                MessageBox.Show("Date/time uploaded to mount.", "Upload date/time", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Views.CopyableMessage.Show("Upload date/time", "Upload failed:\r\n\r\n" + ex.ToString());
            }
        }
    }
}
