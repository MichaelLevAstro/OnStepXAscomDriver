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
    // Single application logger. Driver and hub both call Init at startup; every
    // log line goes through Log() and fans out to:
    //   - LineEmitted event (hub UI subscribes to render the console)
    //   - File under %APPDATA%\OnStepX\logs when VerboseFileLog setting is ON
    // Both processes share one session file so driver+hub timelines interleave
    // in chronological order. The session tag is registered in HKCU on first
    // Init() and reused by the second process.
    internal static class DebugLogger
    {
        private const string RegPath = @"Software\ASCOM\OnStepX";
        private const string VerboseValue = "VerboseFileLog";
        private const string SessionTagValue = "LogSessionTag";
        private const int RetentionDays = 7;
        private static readonly TimeSpan SessionMaxAge = TimeSpan.FromHours(2);

        private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
        private static readonly object _initLock = new object();
        private static CancellationTokenSource _cts;
        private static Task _writerTask;
        private static string _processTag = "?";
        private static int _pid;
        private static string _sessionTag;
        private static string _logDir;
        private static volatile bool _initialized;

        public static event Action<string> LineEmitted;

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

        public static bool VerboseFileLog
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
                _sessionTag = ResolveSessionTag();

                try { Directory.CreateDirectory(LogDirectory); } catch { }
                TryPruneOldFiles();

                _cts = new CancellationTokenSource();
                _writerTask = Task.Run(() => WriterLoop(_cts.Token));

                TransportLogger.Pair += OnTransportPair;

                _initialized = true;
                Log("CONNECT", "Logger init; tag=" + _processTag + " pid=" + _pid + " session=" + _sessionTag);
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
            string line = Format(category, message ?? "");
            try { LineEmitted?.Invoke(line); } catch { }
            if (VerboseFileLog) _queue.Enqueue(line);
        }

        public static void LogException(string category, Exception ex)
        {
            if (ex == null) return;
            Log(category, ex.GetType().Name + ": " + ex.Message);
        }

        private static void OnTransportPair(string cmd, string reply, int elapsedMs)
        {
            if (!_initialized) return;
            string line = Format("IO", cmd + " -> " + reply + "  (" + elapsedMs + " ms)");
            try { LineEmitted?.Invoke(line); } catch { }
            if (VerboseFileLog) _queue.Enqueue(line);
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
                // Locked or unavailable. Drop on the floor rather than block the
                // mount thread — preferable to a stalled slew on a logging issue.
            }
        }

        private static string CurrentFilePath()
        {
            return Path.Combine(LogDirectory, "OnStepX-" + _sessionTag + ".log");
        }

        // Driver and hub agree on the session tag via a registry rendezvous so
        // both processes append to the same file. The first process to start
        // writes its tag; the second one reads and joins. Tags older than
        // SessionMaxAge are treated as stale (e.g. a previous session that
        // crashed without clearing the value).
        private static string ResolveSessionTag()
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RegPath))
                {
                    var existing = k?.GetValue(SessionTagValue) as string;
                    if (!string.IsNullOrEmpty(existing) && IsRecentSessionTag(existing))
                        return existing;
                    string fresh = DateTime.Now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture)
                                 + "_pid" + _pid;
                    k?.SetValue(SessionTagValue, fresh);
                    return fresh;
                }
            }
            catch
            {
                return DateTime.Now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture) + "_pid" + _pid;
            }
        }

        private static bool IsRecentSessionTag(string tag)
        {
            int us = tag.IndexOf('_');
            if (us < 0) return false;
            int us2 = tag.IndexOf('_', us + 1);
            string stamp = us2 > 0 ? tag.Substring(0, us2) : tag;
            if (DateTime.TryParseExact(stamp, "yyyy-MM-dd_HHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var t))
                return (DateTime.Now - t) < SessionMaxAge;
            return false;
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
