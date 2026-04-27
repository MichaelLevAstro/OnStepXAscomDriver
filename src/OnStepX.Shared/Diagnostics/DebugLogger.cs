using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ASCOM.OnStepX.Hardware.Transport;
using Microsoft.Win32;

namespace ASCOM.OnStepX.Diagnostics
{
    // Persistent file-based debug log shared by the driver (in-proc inside NINA
    // et al.) and the hub (separate exe). Both processes call Init at startup;
    // each writes to its own daily file under %APPDATA%\OnStepX\logs so writes
    // never contend on the same handle. The "Open Log Folder" button in the
    // Advanced Settings dialog points users at this directory.
    //
    // Subscribes to TransportLogger.Pair so every wire round-trip is captured
    // for free. A poll-command filter (Verbose I/O off by default) drops the
    // chatty :GR/:GD/:GU/etc. status reads so the file stays useful for
    // post-mortem on multi-minute sequences. Verbose I/O mode logs everything.
    //
    // Settings are read directly from HKCU\Software\ASCOM\OnStepX so the driver
    // process picks them up without needing a project reference on the hub's
    // DriverSettings type.
    internal static class DebugLogger
    {
        private const string RegPath = @"Software\ASCOM\OnStepX";
        private const string EnabledValue = "DebugLogEnabled";
        private const string VerboseValue = "DebugLogVerbosePolls";
        private const int RetentionDays = 7;

        private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
        private static readonly object _initLock = new object();
        private static CancellationTokenSource _cts;
        private static Task _writerTask;
        private static string _processTag = "?";
        private static int _pid;
        private static string _logDir;
        private static volatile bool _initialized;

        public static string LogDirectory
        {
            get
            {
                if (_logDir != null) return _logDir;
                _logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "OnStepX", "logs");
                return _logDir;
            }
        }

        public static bool Enabled
        {
            get => ReadBoolSetting(EnabledValue, true);
            set => WriteBoolSetting(EnabledValue, value);
        }

        public static bool VerbosePolls
        {
            get => ReadBoolSetting(VerboseValue, false);
            set => WriteBoolSetting(VerboseValue, value);
        }

        public static void Init(string processTag)
        {
            lock (_initLock)
            {
                if (_initialized) return;
                _processTag = (processTag ?? "?").ToUpperInvariant();
                try { _pid = Process.GetCurrentProcess().Id; } catch { _pid = 0; }

                try { Directory.CreateDirectory(LogDirectory); } catch { }
                TryPruneOldFiles();

                _cts = new CancellationTokenSource();
                _writerTask = Task.Run(() => WriterLoop(_cts.Token));

                TransportLogger.Pair += OnTransportPair;

                _initialized = true;
                Log("CONNECT", "Logger init; tag=" + _processTag + " pid=" + _pid);
            }
        }

        public static void Shutdown()
        {
            lock (_initLock)
            {
                if (!_initialized) return;
                try { TransportLogger.Pair -= OnTransportPair; } catch { }
                Log("CONNECT", "Logger shutdown; tag=" + _processTag + " pid=" + _pid);
                try { _cts?.Cancel(); } catch { }
                try { _writerTask?.Wait(1000); } catch { }
                FlushQueueOnce();
                _initialized = false;
            }
        }

        public static void Log(string category, string message)
        {
            if (!_initialized) return;
            if (!Enabled) return;
            _queue.Enqueue(Format(category, message ?? ""));
        }

        public static void LogException(string category, Exception ex)
        {
            if (ex == null) return;
            Log(category, ex.GetType().Name + ": " + ex.Message);
        }

        private static void OnTransportPair(string cmd, string reply, int elapsedMs)
        {
            if (!_initialized) return;
            if (!Enabled) return;
            if (!VerbosePolls && IsPollCommand(cmd)) return;
            _queue.Enqueue(Format("IO", cmd + " -> " + reply + "  (" + elapsedMs + " ms)"));
        }

        // Drop the high-frequency status/position reads when not in verbose mode.
        // These come from MountStateCache (hub, ~750 ms) and ASCOM client polls
        // (driver, ~2 Hz). Listed by LX200 prefix; first char after ':'.
        //
        // :Gm# (pier side) and :GU# (status string) are deliberately NOT
        // filtered: pier corruption across :CM# is the primary diagnostic
        // target right now, so every pier read goes to disk. Cost is small —
        // 2 lines per poll cycle vs. 7 polled commands total.
        private static bool IsPollCommand(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return false;
            string s = cmd;
            if (s[0] == ':') s = s.Substring(1);
            if (s.Length == 0) return false;
            // :GRH#, :GR#, :GDH#, :GD#, :GA#, :GZ#, :GS#, :GT#
            if (s.StartsWith("GRH", StringComparison.Ordinal)) return true;
            if (s.StartsWith("GDH", StringComparison.Ordinal)) return true;
            if (s.StartsWith("GR", StringComparison.Ordinal) && !s.StartsWith("GRA")) return true;
            if (s.StartsWith("GD", StringComparison.Ordinal)) return true;
            if (s.StartsWith("GA", StringComparison.Ordinal)) return true;
            if (s.StartsWith("GZ", StringComparison.Ordinal)) return true;
            if (s.StartsWith("GS", StringComparison.Ordinal)) return true;
            if (s.StartsWith("GT", StringComparison.Ordinal)) return true;
            return false;
        }

        private static string Format(string category, string message)
        {
            var now = DateTime.Now;
            return now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                 + " [" + _processTag + " pid=" + _pid + "] ["
                 + (category ?? "MISC") + "] " + message;
        }

        private static void WriterLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                FlushQueueOnce();
                try { Task.Delay(200, ct).Wait(ct); } catch { break; }
            }
            FlushQueueOnce();
        }

        private static void FlushQueueOnce()
        {
            if (_queue.IsEmpty) return;
            string path = CurrentFilePath();
            try
            {
                using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (var sw = new StreamWriter(fs))
                {
                    while (_queue.TryDequeue(out var line)) sw.WriteLine(line);
                }
            }
            catch
            {
                // Drop on the floor rather than reconnect-storm the mount thread.
                // A locked log file is preferable to a dropped slew.
            }
        }

        private static string CurrentFilePath()
        {
            string day = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return Path.Combine(LogDirectory,
                "OnStepX-" + _processTag.ToLowerInvariant() + "-" + day + ".log");
        }

        private static void TryPruneOldFiles()
        {
            try
            {
                if (!Directory.Exists(LogDirectory)) return;
                var cutoff = DateTime.Now.AddDays(-RetentionDays);
                foreach (var f in Directory.EnumerateFiles(LogDirectory, "OnStepX-*.log").ToList())
                {
                    try
                    {
                        if (File.GetLastWriteTime(f) < cutoff) File.Delete(f);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static bool ReadBoolSetting(string name, bool def)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RegPath))
                {
                    if (k == null) return def;
                    var v = k.GetValue(name);
                    if (v == null) return def;
                    return bool.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out var b) ? b : def;
                }
            }
            catch { return def; }
        }

        private static void WriteBoolSetting(string name, bool value)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RegPath))
                {
                    k.SetValue(name, value.ToString());
                }
            }
            catch { }
        }
    }
}
