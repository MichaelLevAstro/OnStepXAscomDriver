using System;
using System.Threading;
using ASCOM.OnStepX.Hardware.State;
using ASCOM.OnStepX.Hardware.Transport;
using ASCOM.OnStepX.Notifications;

namespace ASCOM.OnStepX.Hardware
{
    // Process-wide singleton. Owns the single physical transport and the state cache.
    // Telescope COM objects (per client) reference-count the hardware open state.
    internal sealed class MountSession
    {
        private static readonly Lazy<MountSession> _inst = new Lazy<MountSession>(() => new MountSession());
        public static MountSession Instance => _inst.Value;

        private readonly object _gate = new object();
        private int _openCount;
        private ITransport _transport;
        private volatile bool _responsive;
        public LX200Protocol Protocol { get; private set; }
        public MountStateCache State { get; private set; }

        public event EventHandler ConnectionChanged;

        // Broadcast when a slew is rejected by the firmware with a limit-related
        // code (rc=1 horizon, 2 overhead, 6 outside limits). HubForm subscribes
        // and surfaces a sticky warning in the Limits section. Decoupled from
        // the call site so any UI that issues slews can raise it.
        public event Action<string> LimitWarning;
        public void RaiseLimitWarning(string reason) => LimitWarning?.Invoke(reason);

        // Two-stage connection:
        //   Stage 1 (transport up): USB/TCP handle is open. Useful for UI to show
        //     "Connecting..." with the wire held, but commands to the mount will race
        //     the ESP32 boot / may return garbage.
        //   Stage 2 (responsive):   :GVP# has returned an OnStep identity string, so the
        //     firmware is up and talking LX200. Only now do we start the state-cache poll
        //     and let clients (driver + hub UI) issue commands.
        //
        // Lock-free reads: callers poll these from UI/wait loops while a connect may be
        // probing. Blocking would freeze the STA and dead-lock the hub UI.
        public bool IsTransportOpen { get { var t = _transport; return t != null && t.Connected; } }
        public bool IsMountResponsive => _responsive;
        public bool IsOpen => IsTransportOpen && _responsive;
        public string TransportName { get { return _transport?.DisplayName ?? ""; } }

        private MountSession() { }

        public void Configure(ITransport transport)
        {
            lock (_gate)
            {
                if (_openCount > 0) throw new InvalidOperationException("Cannot reconfigure transport while connected");
                _transport?.Dispose();
                _transport = transport;
                Protocol = new LX200Protocol(_transport);
                State?.Dispose();
                State = new MountStateCache(Protocol);
            }
        }

        // Two-stage open. Caller owns cancellation; Disconnect cancels the token and the
        // probe loop exits promptly. The lock is released across the probe so Close() can
        // tear down the transport while we wait.
        public void Open(CancellationToken ct = default)
        {
            ITransport t;
            lock (_gate)
            {
                if (_transport == null) throw new InvalidOperationException("Transport not configured");
                if (_openCount > 0)
                {
                    _openCount++;
                    return;
                }
                t = _transport;
            }

            bool transportOpenedHere = false;
            try
            {
                if (!t.Connected)
                {
                    t.Open();
                    transportOpenedHere = true;
                }
                // Stage 1: wire is up but mount not yet known responsive. Fire event so the
                // UI can keep its "Connecting..." state without flipping to Connected.
                ConnectionChanged?.Invoke(this, EventArgs.Empty);

                // Stage 2: probe :GVP# until we hear OnStep back (or caller cancels).
                ProbeUntilResponsive(t, ct);

                lock (_gate)
                {
                    if (_transport != t) throw new OperationCanceledException("Transport replaced during open");
                    _responsive = true;
                    _openCount++;
                    State?.Start();
                }
                ConnectionChanged?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                _responsive = false;
                if (transportOpenedHere) { try { t.Close(); } catch { } }
                ConnectionChanged?.Invoke(this, EventArgs.Empty);
                throw;
            }
        }

        // 30-second overall budget. Short per-probe timeouts catch each boot window; a
        // mid-budget transport recycle handles MCUs that stayed silent through a DTR-
        // triggered reset. Drains between probes so a late reply from a timed-out send
        // cannot poison the next read. Caller can also cancel early via the token.
        private void ProbeUntilResponsive(ITransport t, CancellationToken ct)
        {
            const int overallBudgetMs = 30000;
            const int perProbeTimeoutMs = 800;
            const int betweenProbeMs = 500;
            const int recycleAfterMs = 12000;

            int savedTimeout = t.TimeoutMs;
            t.TimeoutMs = perProbeTimeoutMs;
            int overallDeadline = Environment.TickCount + overallBudgetMs;
            int phaseStart = Environment.TickCount;
            int probes = 0;
            string lastReply = null;
            Exception lastEx = null;
            try
            {
                while (Environment.TickCount < overallDeadline)
                {
                    ct.ThrowIfCancellationRequested();
                    probes++;
                    string reply = null;
                    try { reply = t.SendAndReceive(":GVP#"); }
                    catch (Exception ex) { lastEx = ex; }
                    lastReply = reply;
                    string clean = (reply ?? "").Trim().TrimEnd('#').Trim();
                    if (clean.IndexOf("On-Step", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        clean.IndexOf("OnStep", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        TransportLogger.Note("Mount responsive after " + probes + " probe(s): " + clean);
                        return;
                    }
                    if (t is Transport.SerialTransport s) s.DrainUntilQuiet(120, 400);

                    if (Environment.TickCount - phaseStart > recycleAfterMs &&
                        Environment.TickCount + 3000 < overallDeadline)
                    {
                        TransportLogger.Note("Probe budget elapsed; recycling transport and retrying");
                        try { t.Close(); } catch { }
                        WaitCancellable(1500, ct);
                        t.Open();
                        t.TimeoutMs = perProbeTimeoutMs;
                        phaseStart = Environment.TickCount;
                        continue;
                    }

                    WaitCancellable(betweenProbeMs, ct);
                }

                throw new TimeoutException(
                    "Mount did not respond to :GVP# within " + (overallBudgetMs / 1000) + "s (" +
                    probes + " probe(s)). Last reply was " +
                    (string.IsNullOrEmpty(lastReply) ? "(empty)" : "'" + lastReply + "'") +
                    ". Verify port/host, baud rate, and that the device is an OnStepX." +
                    (lastEx != null ? "\r\n\r\nLast error: " + lastEx.Message : ""));
            }
            finally { try { t.TimeoutMs = savedTimeout; } catch { } }
        }

        private static void WaitCancellable(int ms, CancellationToken ct)
        {
            if (ct.WaitHandle.WaitOne(ms)) ct.ThrowIfCancellationRequested();
        }

        public void Close()
        {
            lock (_gate)
            {
                if (_openCount == 0)
                {
                    _responsive = false;
                    return;
                }
                _openCount--;
                if (_openCount == 0)
                {
                    State?.Stop();
                    _responsive = false;
                    try { _transport?.Close(); } catch { }
                }
            }
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        // Raw command pass-throughs for the manual-command UI and the pipe
        // server (driver process delegates every wire byte through here, so
        // this is also the chokepoint for ASCOM-originated traffic). Blocked
        // until stage 2 completes so in-flight probes don't collide with
        // client CMDs.
        //
        // Sync gate: any :CM#-class command runs through SyncLimitGuard before
        // hitting the wire. Lock is released around the guard prompt so the
        // hub UI doesn't freeze while waiting on the user.
        public string SendAndReceiveRaw(string command)
        {
            if (SyncLimitGuard.IsSyncCommand(command) && !SyncLimitGuard.ShouldAllowSync(Protocol, State))
                return "";
            lock (_gate)
            {
                if (_transport == null || !_transport.Connected || !_responsive)
                    throw new InvalidOperationException("Not connected");
                return _transport.SendAndReceive(command);
            }
        }
        public void SendBlindRaw(string command)
        {
            if (SyncLimitGuard.IsSyncCommand(command) && !SyncLimitGuard.ShouldAllowSync(Protocol, State))
                return;
            lock (_gate)
            {
                if (_transport == null || !_transport.Connected || !_responsive)
                    throw new InvalidOperationException("Not connected");
                _transport.SendBlind(command);
            }
        }

        public void ForceCloseAll()
        {
            lock (_gate)
            {
                _openCount = 0;
                _responsive = false;
                State?.Stop();
                try { _transport?.Close(); } catch { }
            }
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
