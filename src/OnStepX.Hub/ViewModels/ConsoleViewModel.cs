using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Diagnostics;
using ASCOM.OnStepX.Hardware;
using ASCOM.OnStepX.Hardware.Transport;

namespace ASCOM.OnStepX.ViewModels
{
    // Console card. Mirrors HubForm.BuildLogPanel + BuildIoPane + DrainIo +
    // RebuildIoView + AppendColoredLine + SendManualCommand. The legacy
    // RichTextBox + manual SelectionColor approach is replaced with an
    // ObservableCollection<ConsoleEntry> projected into a virtualized
    // ItemsControl; filter is a CollectionView Filter predicate.
    public sealed class ConsoleViewModel : ViewModelBase
    {
        private readonly MainViewModel _main;
        private readonly MountSession _mount = MountSession.Instance;
        private const int HistoryMax = 2000;

        private readonly ObservableCollection<ConsoleEntry> _entries = new ObservableCollection<ConsoleEntry>();
        public ICollectionView Entries { get; }

        private readonly ConcurrentQueue<ConsoleEntry> _pending = new ConcurrentQueue<ConsoleEntry>();

        private bool _ioEnabled = true;
        public bool IoEnabled { get => _ioEnabled; set => Set(ref _ioEnabled, value); }

        private bool _autoScroll = true;
        public bool AutoScroll
        {
            get => _autoScroll;
            set { if (Set(ref _autoScroll, value) && value) AutoScrollRequested?.Invoke(this, EventArgs.Empty); }
        }
        public event EventHandler AutoScrollRequested;

        private string _filter = "";
        public string Filter
        {
            get => _filter;
            set
            {
                if (!Set(ref _filter, value ?? "")) return;
                Entries.Refresh();
            }
        }

        private bool _consoleVisible = DriverSettings.ConsoleVisible;
        public bool ConsoleVisible
        {
            get => _consoleVisible;
            set
            {
                if (Set(ref _consoleVisible, value))
                {
                    try { DriverSettings.ConsoleVisible = value; } catch { }
                }
            }
        }

        private string _commandInput = "";
        public string CommandInput { get => _commandInput; set => Set(ref _commandInput, value); }

        public bool MountActionsEnabled => _main.State == ConnState.Connected;

        public ICommand ClearCommand { get; }
        public ICommand CopyCommand { get; }
        public ICommand ClearFilterCommand { get; }
        public ICommand SendCommand { get; }

        public ConsoleViewModel(MainViewModel main)
        {
            _main = main;
            Entries = CollectionViewSource.GetDefaultView(_entries);
            Entries.Filter = MatchesFilter;

            ClearCommand = new RelayCommand(() =>
            {
                _entries.Clear();
                while (_pending.TryDequeue(out _)) { }
            });
            CopyCommand = new RelayCommand(() =>
            {
                try
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (ConsoleEntry e in Entries) sb.AppendLine(e.Raw);
                    if (sb.Length > 0) Clipboard.SetText(sb.ToString());
                }
                catch { }
            });
            ClearFilterCommand = new RelayCommand(() => Filter = "");
            SendCommand = new RelayCommand(SendManualCommand, () => MountActionsEnabled && !string.IsNullOrWhiteSpace(CommandInput));

            // Hook the same events HubForm subscribes to in its constructor.
            TransportLogger.Pair += OnTransportPair;
            TransportLogger.Line += OnTransportLine;
            DebugLogger.LineEmitted += OnLoggerLine;
        }

        public void Detach()
        {
            TransportLogger.Pair -= OnTransportPair;
            TransportLogger.Line -= OnTransportLine;
            DebugLogger.LineEmitted -= OnLoggerLine;
        }

        internal void OnConnStateChanged()
        {
            OnPropertyChanged(nameof(MountActionsEnabled));
            CommandManager.InvalidateRequerySuggested();
        }

        private bool MatchesFilter(object o)
        {
            if (string.IsNullOrEmpty(_filter)) return true;
            var e = (ConsoleEntry)o;
            string raw = e.Raw ?? "";
            foreach (var tok in _filter.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (raw.IndexOf(tok, StringComparison.Ordinal) < 0) return false;
            return true;
        }

        // ─── transport / logger event wiring ───
        private void OnTransportPair(string cmd, string reply, int elapsedMs)
        {
            if (!_ioEnabled) return;
            string ts = DateTime.Now.ToString("HH:mm:ss.fff");
            string elapsed = elapsedMs > 0 ? "(" + elapsedMs + " ms)" : "";
            string raw = ts + "  " + (cmd ?? "").PadRight(14) + "  ->  " + reply +
                         (elapsedMs > 0 ? "  (" + elapsedMs + " ms)" : "");
            var entry = new ConsoleEntry
            {
                Timestamp = ts,
                Command = cmd ?? "",
                Response = reply ?? "",
                Elapsed = elapsed,
                Kind = IsReplyValid(cmd, reply) ? ConsoleEntryKind.Pair : ConsoleEntryKind.Invalid,
                IsBlind = reply == "(blind)",
                Raw = raw
            };
            _pending.Enqueue(entry);
            ScheduleDrain();
        }

        private void OnTransportLine(string rawLine)
        {
            if (!_ioEnabled || rawLine == null || !rawLine.StartsWith("-- ")) return;
            string ts = DateTime.Now.ToString("HH:mm:ss.fff");
            string raw = ts + "  " + rawLine;
            _pending.Enqueue(new ConsoleEntry
            {
                Timestamp = ts,
                Command = "",
                Response = rawLine,
                Elapsed = "",
                Kind = ConsoleEntryKind.Note,
                Raw = raw
            });
            ScheduleDrain();
        }

        private void OnLoggerLine(string formattedLine)
        {
            if (!_ioEnabled || string.IsNullOrEmpty(formattedLine)) return;
            // Filter out the hub-process IO lines (covered by OnTransportPair).
            if (formattedLine.IndexOf("[HUB ", StringComparison.Ordinal) > 0 &&
                formattedLine.IndexOf("] [IO]", StringComparison.Ordinal) > 0) return;
            _pending.Enqueue(new ConsoleEntry
            {
                Timestamp = "",
                Command = "",
                Response = formattedLine,
                Elapsed = "",
                Kind = ConsoleEntryKind.Note,
                Raw = formattedLine
            });
            ScheduleDrain();
        }

        private bool _drainScheduled;
        private void ScheduleDrain()
        {
            if (_drainScheduled) return;
            _drainScheduled = true;
            Application.Current?.Dispatcher.BeginInvoke(new Action(Drain),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private void Drain()
        {
            _drainScheduled = false;
            while (_pending.TryDequeue(out var entry))
            {
                _entries.Add(entry);
                if (_entries.Count > HistoryMax) _entries.RemoveAt(0);
            }
            if (_autoScroll) AutoScrollRequested?.Invoke(this, EventArgs.Empty);
        }

        // ─── manual command ───
        private async void SendManualCommand()
        {
            if (_main.State != ConnState.Connected)
            {
                MessageBox.Show("Not connected.", "Manual command", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string raw = (CommandInput ?? "").Trim();
            if (raw.Length == 0) return;
            if (!raw.StartsWith(":")) raw = ":" + raw;
            if (!raw.EndsWith("#"))   raw = raw + "#";
            string cmd = raw;

            string reply = null; Exception err = null;
            try { reply = await Task.Run(() => _mount.SendAndReceiveRaw(cmd)); }
            catch (Exception ex) { err = ex; }

            if (err != null)
                Views.CopyableMessage.Show("Manual command", "Command '" + cmd + "' failed:\r\n\r\n" + err.ToString());
        }

        // ─── reply validation (ports HubForm.IsReplyValid + ClassifyReply) ───
        private enum ReplyKind { Unknown, Ack01, SlewAck, Numeric, NonEmpty }

        private static ReplyKind ClassifyReply(string cmdCore)
        {
            if (string.IsNullOrEmpty(cmdCore)) return ReplyKind.Unknown;
            if (cmdCore[0] == 'S') return ReplyKind.Ack01;
            if (cmdCore == "Te" || cmdCore == "Td") return ReplyKind.Ack01;
            if (cmdCore == "hP" || cmdCore == "hR" || cmdCore == "hQ") return ReplyKind.Ack01;
            if (cmdCore == "MS" || cmdCore == "MA") return ReplyKind.SlewAck;
            switch (cmdCore)
            {
                case "GT": case "GS": case "GR": case "GRH": case "GD": case "GDH":
                case "GA": case "GZ": case "GL": case "Gt": case "GtH":
                case "Gg": case "GgH": case "GG": case "GC": case "Gv": case "Gh":
                    return ReplyKind.Numeric;
            }
            if (cmdCore.StartsWith("GX9", StringComparison.Ordinal)) return ReplyKind.Numeric;
            if (cmdCore.StartsWith("GXE", StringComparison.Ordinal)) return ReplyKind.Numeric;
            switch (cmdCore)
            {
                case "GVP": case "GVN": case "GVM": case "GVD":
                case "GU": case "Gm": case "GW": case "GE": case "Go":
                    return ReplyKind.NonEmpty;
            }
            return ReplyKind.Unknown;
        }

        private static string CmdCore(string cmd)
        {
            var c = cmd ?? "";
            if (c.StartsWith(":")) c = c.Substring(1);
            if (c.EndsWith("#"))   c = c.Substring(0, c.Length - 1);
            int comma = c.IndexOf(',');
            if (comma >= 0) c = c.Substring(0, comma);
            return c;
        }

        private static bool IsReplyValid(string cmd, string reply)
        {
            if (reply == "(blind)") return true;
            var r = (reply ?? "").Trim().TrimEnd('#').Trim();
            var kind = ClassifyReply(CmdCore(cmd));
            switch (kind)
            {
                case ReplyKind.Ack01:    return r == "1";
                case ReplyKind.SlewAck:  return r.Length > 0 && r[0] == '0';
                case ReplyKind.Numeric:
                    if (r.Length == 0) return false;
                    if (r.Equals("nan", StringComparison.OrdinalIgnoreCase)) return false;
                    foreach (var ch in r) if (ch >= '0' && ch <= '9') return true;
                    return false;
                case ReplyKind.NonEmpty: return r.Length > 0;
                default: return true;
            }
        }
    }
}
