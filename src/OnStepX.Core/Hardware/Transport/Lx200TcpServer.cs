using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ASCOM.OnStepX.Diagnostics;

namespace ASCOM.OnStepX.Hardware.Transport
{
    // Presents the mount on TCP as if it were OnStepX's own WiFi interface, so
    // KStars/Ekos can drive it with the stock indi_lx200_OnStep driver while
    // the hub keeps ownership of the serial port.
    //
    // This is why there is no custom INDI driver: INDI already has a good one
    // for OnStep, and it can connect to a network mount. The only thing
    // missing was something for it to connect to.
    //
    // Everything goes through MountSession's raw send, which is the same
    // chokepoint the ASCOM pipe uses — so Ekos gets the hub's sync-limit guard
    // and console logging for free, and its traffic is serialised against the
    // poll loop rather than racing it.
    //
    // Binds to loopback unless told otherwise. Ekos normally runs on the same
    // Pi, and an unauthenticated port that can slew a telescope is not
    // something to put on a network by default.
    internal sealed class Lx200TcpServer : IDisposable
    {
        public const int DefaultPort = 9624;

        private readonly MountSession _mount;
        private readonly int _port;
        private readonly bool _allowRemote;

        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private int _clients;

        public Lx200TcpServer(MountSession mount, int port = DefaultPort, bool allowRemote = false)
        {
            _mount = mount ?? throw new ArgumentNullException(nameof(mount));
            _port = port;
            _allowRemote = allowRemote;
        }

        public bool IsRunning { get; private set; }
        public int ClientCount => Volatile.Read(ref _clients);
        public int Port => _port;

        public event Action ClientsChanged;

        public void Start()
        {
            if (IsRunning) return;

            try
            {
                var address = _allowRemote ? IPAddress.Any : IPAddress.Loopback;
                _listener = new TcpListener(address, _port);
                _listener.Start();
                _cts = new CancellationTokenSource();
                IsRunning = true;

                DebugLogger.Log("LX200", string.Format(
                    "LX200 bridge listening on {0}:{1} — point indi_lx200_OnStep at it as a network mount",
                    address, _port));

                Task.Run(() => AcceptLoop(_cts.Token));
            }
            catch (Exception ex)
            {
                IsRunning = false;
                DebugLogger.LogException("LX200", ex);
                try
                {
                    TransportLogger.Note("LX200 bridge could not listen on port " + _port + ": " + ex.Message);
                }
                catch { }
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            DebugLogger.Log("LX200", "LX200 bridge stopped");
        }

        public void Dispose()
        {
            Stop();
            try { _cts?.Dispose(); } catch { }
        }

        private async Task AcceptLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client = null;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch
                {
                    if (token.IsCancellationRequested) return;
                    await Task.Delay(200, token).ConfigureAwait(false);
                    continue;
                }

                var c = client;
                _ = Task.Run(() => ServeClient(c, token), token);
            }
        }

        private void ServeClient(TcpClient client, CancellationToken token)
        {
            Interlocked.Increment(ref _clients);
            RaiseClientsChanged();

            string who = "?";
            try { who = client.Client.RemoteEndPoint?.ToString() ?? "?"; } catch { }
            DebugLogger.Log("LX200", "Client connected: " + who);

            try
            {
                client.NoDelay = true;
                using (var stream = client.GetStream())
                {
                    var buffer = new byte[512];
                    var pending = new StringBuilder();

                    while (!token.IsCancellationRequested)
                    {
                        int read;
                        try { read = stream.Read(buffer, 0, buffer.Length); }
                        catch { break; }
                        if (read <= 0) break;

                        var chunk = Encoding.ASCII.GetString(buffer, 0, read);

                        // The LX200 ACK handshake. A client opens with a bare
                        // 0x06 and waits for one character naming the mount's
                        // alignment — no '#', no colon. indi_lx200_OnStep
                        // sends it twice, five seconds apart, before it will
                        // send anything else, so a bridge that only forwards
                        // ':...#' frames never gets a client connected.
                        //
                        // Answered here rather than forwarded: the reply is
                        // unterminated, and the mount transport frames on '#',
                        // so passing it through would stall for the read
                        // timeout on every one.
                        int ack;
                        while ((ack = chunk.IndexOf('\u0006')) >= 0)
                        {
                            chunk = chunk.Remove(ack, 1);
                            var reply = Encoding.ASCII.GetBytes(AlignmentCharacter());
                            try { stream.Write(reply, 0, reply.Length); }
                            catch { return; }
                        }

                        pending.Append(chunk);

                        foreach (var command in TakeCommands(pending))
                        {
                            string reply = Dispatch(command);
                            if (reply == null) continue;

                            var bytes = Encoding.ASCII.GetBytes(reply);
                            try { stream.Write(bytes, 0, bytes.Length); }
                            catch { return; }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogException("LX200", ex);
            }
            finally
            {
                try { client.Close(); } catch { }
                Interlocked.Decrement(ref _clients);
                RaiseClientsChanged();
                DebugLogger.Log("LX200", "Client disconnected: " + who);
            }
        }

        // Pulls whole ':...#' frames out of the buffer, discarding anything
        // before a ':' so a half-received or malformed frame cannot wedge the
        // stream. The six-byte ACK handshake some LX200 clients open with has
        // no '#', which is why leading junk is dropped rather than kept.
        private static IEnumerable<string> TakeCommands(StringBuilder pending)
        {
            var found = new List<string>();

            while (true)
            {
                string s = pending.ToString();
                int start = s.IndexOf(':');
                if (start < 0)
                {
                    pending.Clear();
                    break;
                }

                int end = s.IndexOf('#', start + 1);
                if (end < 0)
                {
                    // Keep the partial frame for the next read.
                    if (start > 0) { pending.Clear(); pending.Append(s.Substring(start)); }
                    break;
                }

                found.Add(s.Substring(start, end - start + 1));
                pending.Clear();
                pending.Append(s.Substring(end + 1));
            }

            return found;
        }

        // What OnStep answers the ACK with: 'A' for ALTAZM, 'P' otherwise.
        // Straight out of the firmware's BufferCmds, which rewrites a bare
        // 0x06 into a command carrying that character.
        private string AlignmentCharacter()
        {
            try
            {
                if (_mount.IsOpen && _mount.Protocol != null)
                    return _mount.Protocol.GetMountType() == 3 ? "A" : "P";
            }
            catch (Exception ex)
            {
                DebugLogger.LogException("LX200", ex);
            }

            // Equatorial is the safe assumption: it is what every mount this
            // hub has driven is, and an ALTAZM reported as equatorial is a
            // wrong label rather than a wrong movement.
            return "P";
        }

        // Returns the reply to write back, or null when the command is one the
        // mount answers with nothing.
        private string Dispatch(string command)
        {
            try
            {
                if (!_mount.IsOpen)
                {
                    // Nothing useful to say: the client is talking to a hub
                    // whose mount link is down. Staying silent lets its own
                    // timeout surface it rather than inventing a reading.
                    DebugLogger.Log("LX200", "Dropped (mount not connected): " + command);
                    return null;
                }

                if (Lx200CommandSet.IsBlind(command))
                {
                    _mount.SendBlindRaw(command);
                    return null;
                }

                return _mount.SendAndReceiveRaw(command);
            }
            catch (Exception ex)
            {
                DebugLogger.LogException("LX200", ex);
                return null;
            }
        }

        private void RaiseClientsChanged()
        {
            try { ClientsChanged?.Invoke(); } catch { }
        }
    }
}
