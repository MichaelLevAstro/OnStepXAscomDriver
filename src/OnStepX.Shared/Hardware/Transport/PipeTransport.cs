using System;
using System.IO;
using System.IO.Pipes;

namespace ASCOM.OnStepX.Hardware.Transport
{
    // ITransport that forwards LX200 commands to the primary hub process over a
    // named pipe. Used by /embedding proxy instances that COM SCM spawns when
    // the user-launched hub is already running: SCM's second exe cannot open
    // the same COM port or share the same MountSession singleton, so mount I/O
    // has to hop over a pipe to the process that actually owns the wire.
    //
    // Wire protocol (one line per request, one line per reply, tab-delimited):
    //   CMD\t<lx200>      -> OK\t<reply>   or ERR\t<msg>
    //   BLIND\t<lx200>    -> OK            or ERR\t<msg>
    //   IPC:ISCONNECTED   -> IPC:ISCONNECTED:TRUE|FALSE
    internal sealed class PipeTransport : ITransport
    {
        public const string PIPE_NAME = "OnStepXHub";

        private readonly string _pipeName;
        private NamedPipeClientStream _pipe;
        private StreamReader _reader;
        private StreamWriter _writer;
        private readonly object _lock = new object();

        public PipeTransport(string pipeName = PIPE_NAME) { _pipeName = pipeName; }

        public bool Connected => _pipe != null && _pipe.IsConnected;
        public string DisplayName => "pipe:" + _pipeName;
        public int TimeoutMs { get; set; } = 5000;

        public void Open()
        {
            lock (_lock)
            {
                if (Connected) return;
                _pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
                try { _pipe.Connect(Math.Max(500, TimeoutMs)); }
                catch (TimeoutException)
                {
                    Cleanup();
                    throw new TimeoutException(
                        "OnStepX hub pipe '" + _pipeName + "' is not reachable. " +
                        "Make sure the OnStepX hub window is open.");
                }
                _reader = new StreamReader(_pipe);
                _writer = new StreamWriter(_pipe) { AutoFlush = true };

                // Handshake: the pipe being up only means the hub exe is running — it does
                // not guarantee the hub has connected to the physical mount. Reject Open()
                // now so callers see NotConnected cleanly instead of a cascade of failures
                // on every subsequent LX200 command.
                _writer.WriteLine("IPC:ISCONNECTED");
                string reply;
                try { reply = _reader.ReadLine() ?? ""; }
                catch (IOException)
                {
                    Cleanup();
                    throw new IOException("OnStepX hub closed pipe during handshake");
                }
                if (reply != "IPC:ISCONNECTED:TRUE")
                {
                    Cleanup();
                    throw new InvalidOperationException(
                        "OnStepX hub is running but not connected to the mount. " +
                        "Open the hub and click Connect before connecting this client.");
                }
            }
        }

        public void Close() { lock (_lock) { Cleanup(); } }
        public void Dispose() => Close();

        public string SendAndReceive(string command)
        {
            lock (_lock)
            {
                if (!Connected) throw new InvalidOperationException("Pipe not open");
                TransportLogger.Tx(command);
                int start = Environment.TickCount;
                string line;
                try
                {
                    _writer.WriteLine("CMD\t" + command);
                    line = _reader.ReadLine();
                }
                catch (IOException ex)
                {
                    Cleanup();
                    throw new IOException("OnStepX hub pipe I/O failed: " + ex.Message, ex);
                }
                int elapsed = Environment.TickCount - start;
                string reply = ParseReply(line);
                TransportLogger.Rx(reply, elapsed);
                TransportLogger.PairEvent(command, reply, elapsed);
                return reply;
            }
        }

        public void SendBlind(string command)
        {
            lock (_lock)
            {
                if (!Connected) throw new InvalidOperationException("Pipe not open");
                TransportLogger.Tx(command);
                string line;
                try
                {
                    _writer.WriteLine("BLIND\t" + command);
                    line = _reader.ReadLine();
                }
                catch (IOException ex)
                {
                    Cleanup();
                    throw new IOException("OnStepX hub pipe I/O failed: " + ex.Message, ex);
                }
                TransportLogger.BlindEvent(command);
                ParseReply(line); // ignore result; throws on ERR
            }
        }

        // Out-of-band IPC: ask the hub to pop its window to the foreground.
        // Unlike CMD/BLIND, carries no LX200 payload. Fire-and-forget from the
        // driver's perspective; hub acknowledges with "OK".
        public void ShowHub()
        {
            lock (_lock)
            {
                if (!Connected) return;
                try
                {
                    _writer.WriteLine("IPC:SHOWHUB");
                    _reader.ReadLine();
                }
                catch (IOException) { Cleanup(); }
            }
        }

        private static string ParseReply(string line)
        {
            if (line == null) throw new IOException("OnStepX hub closed pipe");
            if (line == "OK") return "";
            if (line.StartsWith("OK\t")) return line.Substring(3);
            if (line.StartsWith("ERR\t"))
                throw new InvalidOperationException("Hub: " + line.Substring(4));
            throw new IOException("Malformed pipe reply: '" + line + "'");
        }

        private void Cleanup()
        {
            try { _writer?.Dispose(); } catch { }
            try { _reader?.Dispose(); } catch { }
            try { _pipe?.Dispose(); } catch { }
            _writer = null; _reader = null; _pipe = null;
        }
    }
}
