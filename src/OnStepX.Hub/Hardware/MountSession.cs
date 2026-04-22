using System;
using System.Threading;
using ASCOM.OnStepX.Hardware.State;
using ASCOM.OnStepX.Hardware.Transport;

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
        public LX200Protocol Protocol { get; private set; }
        public MountStateCache State { get; private set; }

        public event EventHandler ConnectionChanged;

        // Lock-free reads: callers poll this from UI/wait loops while Open() may be holding
        // the gate for a ~30s probe. Blocking here would freeze the STA and dead-lock the
        // hub UI. Reference is assigned atomically; the transport's own Connected flag is
        // thread-safe per contract.
        public bool IsOpen { get { var t = _transport; return t != null && t.Connected; } }
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

        public void Open()
        {
            lock (_gate)
            {
                if (_transport == null) throw new InvalidOperationException("Transport not configured");
                if (_openCount == 0 && !_transport.Connected)
                {
                    // Two open cycles handle the ESP32 auto-reset pattern the user reliably
                    // reproduces: first Open() pulses DTR, MCU reboots, :GVP# probes race boot
                    // and time out. Closing and reopening after a settle window either catches
                    // a now-booted MCU, or triggers a faster second reset that our longer
                    // probe budget can ride out. This mirrors what the user does manually.
                    Exception firstErr = null;
                    bool opened = false;
                    for (int cycle = 0; cycle < 2 && !opened; cycle++)
                    {
                        try
                        {
                            _transport.Open();
                            ProbeForOnStep(bootBudgetMs: cycle == 0 ? 12000 : 15000);
                            opened = true;
                        }
                        catch (Exception ex)
                        {
                            if (firstErr == null) firstErr = ex;
                            try { _transport.Close(); } catch { }
                            if (cycle == 0)
                            {
                                TransportLogger.Note("Open cycle 1 failed (" + ex.Message.Split('\n')[0] + "); waiting for MCU boot then retrying");
                                Thread.Sleep(2500);
                            }
                        }
                    }
                    if (!opened) throw firstErr;
                }
                if (_openCount == 0) State?.Start();
                _openCount++;
            }
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        // Time-budgeted probe for OnStepX identity via :GVP#. Sends repeated probes with a
        // short per-probe timeout (so we get many chances to hit the boot window), drains
        // residual bytes between probes (so a late reply from a timed-out send can't poison
        // the next read), and accepts "On-Step" / "OnStep" / "OnStepX" product strings.
        private void ProbeForOnStep(int bootBudgetMs)
        {
            const int perProbeTimeoutMs = 800;
            const int betweenProbeMs = 250;
            int savedTimeout = _transport.TimeoutMs;
            _transport.TimeoutMs = perProbeTimeoutMs;
            string lastReply = null;
            Exception lastEx = null;
            int deadline = Environment.TickCount + bootBudgetMs;
            int probe = 0;
            try
            {
                while (Environment.TickCount < deadline)
                {
                    probe++;
                    try
                    {
                        lastReply = _transport.SendAndReceive(":GVP#");
                        string clean = (lastReply ?? "").Trim().TrimEnd('#').Trim();
                        if (clean.IndexOf("On-Step", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            clean.IndexOf("OnStep",  StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            TransportLogger.Note("Mount identity OK after " + probe + " probe(s): " + clean);
                            return;
                        }
                    }
                    catch (Exception ex) { lastEx = ex; }
                    if (_transport is Transport.SerialTransport s) s.DrainUntilQuiet(120, 400);
                    Thread.Sleep(betweenProbeMs);
                }
            }
            finally { _transport.TimeoutMs = savedTimeout; }

            throw new InvalidOperationException(
                "Mount identity check timed out after " + probe + " probe(s) in " + bootBudgetMs + "ms. " +
                "Last :GVP# reply was " +
                (string.IsNullOrEmpty(lastReply) ? "(empty)" : "'" + lastReply + "'") +
                ". Expected 'On-Step'. Verify port/host, baud rate, and that the device is an OnStepX." +
                (lastEx != null ? "\r\n\r\nLast error: " + lastEx.Message : ""));
        }

        public void Close()
        {
            lock (_gate)
            {
                if (_openCount == 0) return;
                _openCount--;
                if (_openCount == 0)
                {
                    State?.Stop();
                    try { _transport?.Close(); } catch { }
                }
            }
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        // Raw command pass-throughs for the manual-command UI. Returns reply for SendAndReceive.
        public string SendAndReceiveRaw(string command)
        {
            lock (_gate)
            {
                if (_transport == null || !_transport.Connected)
                    throw new InvalidOperationException("Not connected");
                return _transport.SendAndReceive(command);
            }
        }
        public void SendBlindRaw(string command)
        {
            lock (_gate)
            {
                if (_transport == null || !_transport.Connected)
                    throw new InvalidOperationException("Not connected");
                _transport.SendBlind(command);
            }
        }

        public void ForceCloseAll()
        {
            lock (_gate)
            {
                _openCount = 0;
                State?.Stop();
                try { _transport?.Close(); } catch { }
            }
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
