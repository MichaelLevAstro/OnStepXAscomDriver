using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using ASCOM.OnStepX.Diagnostics;

namespace ASCOM.OnStepX.Hardware.Transport
{
    // Named-pipe server run by the hub process. Accepts connections from any
    // number of in-process ASCOM driver clients (each client's process loads
    // ASCOM.OnStepX.Telescope.dll, which opens its own PipeTransport back to
    // this server) and forwards each LX200 command to the single MountSession
    // that owns the wire. Commands are serialized by MountSession itself;
    // multiple client connections are handled in parallel on the thread pool.
    internal sealed class HubPipeServer : IDisposable
    {
        public const string PIPE_NAME = PipeTransport.PIPE_NAME;

        private readonly MountSession _mount;
        private readonly Action _showHubHandler;
        private CancellationTokenSource _cts;
        private bool _running;

        public HubPipeServer(MountSession mount, Action showHubHandler = null)
        {
            _mount = mount;
            _showHubHandler = showHubHandler;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _cts = new CancellationTokenSource();
            Task.Run(() => AcceptLoop(_cts.Token));
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            try { _cts?.Cancel(); } catch { }
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
                NamedPipeServerStream pipe = null;
                try
                {
                    pipe = new NamedPipeServerStream(
                        PIPE_NAME,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await Task.Factory.FromAsync(pipe.BeginWaitForConnection, pipe.EndWaitForConnection, null);

                    if (token.IsCancellationRequested) { try { pipe.Dispose(); } catch { } break; }

                    var clientPipe = pipe;
                    pipe = null;
                    _ = Task.Run(() => HandleClient(clientPipe, token))
                        .ContinueWith(_ => { try { clientPipe.Dispose(); } catch { } },
                                      TaskContinuationOptions.OnlyOnFaulted);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (IOException)
                {
                    try { await Task.Delay(100, token); } catch { break; }
                }
                finally { try { pipe?.Dispose(); } catch { } }
            }
        }

        private async Task HandleClient(NamedPipeServerStream pipe, CancellationToken token)
        {
            // Count driver pipe connections as ASCOM clients — this is what
            // the hub UI "Connected clients" label and the "clients still
            // connected" close-guard prompt consume. Incremented only once we
            // have a confirmed working pipe so probe attempts don't inflate
            // the count.
            State.ClientRegistry.Add();
            try
            {
                using (var reader = new StreamReader(pipe))
                using (var writer = new StreamWriter(pipe) { AutoFlush = true })
                {
                    while (!token.IsCancellationRequested && pipe.IsConnected)
                    {
                        string line;
                        try { line = await reader.ReadLineAsync(); }
                        catch { break; }
                        if (line == null) break;

                        string response = Dispatch(line);
                        try { await writer.WriteLineAsync(response); }
                        catch { break; }
                    }
                }
            }
            catch { }
            finally
            {
                try { pipe.Dispose(); } catch { }
                State.ClientRegistry.Remove();
            }
        }

        private string Dispatch(string line)
        {
            try
            {
                if (line == "IPC:ISCONNECTED")
                {
                    bool open = _mount.IsOpen;
                    DebugLogger.Log("IPC", "received IPC:ISCONNECTED -> " + (open ? "TRUE" : "FALSE"));
                    return "IPC:ISCONNECTED:" + (open ? "TRUE" : "FALSE");
                }

                if (line == "IPC:SHOWHUB")
                {
                    DebugLogger.Log("IPC", "received IPC:SHOWHUB");
                    try { _showHubHandler?.Invoke(); } catch { }
                    return "OK";
                }

                if (line.StartsWith("IPC:VERSION\t"))
                {
                    // Optional handshake. Legacy hubs (v0.3.16 proxy mode) do
                    // not implement this and reply ERR; driver tolerates that.
                    var ver = typeof(HubPipeServer).Assembly.GetName().Version?.ToString() ?? "0.0.0";
                    DebugLogger.Log("IPC", "received IPC:VERSION clientVer='" + line.Substring("IPC:VERSION\t".Length) + "' replyHubVer=" + ver);
                    return "OK\t" + ver;
                }

                if (line.StartsWith("CMD\t"))
                {
                    string cmd = line.Substring(4);
                    if (!_mount.IsOpen) return "ERR\tmount not connected";
                    string reply = _mount.SendAndReceiveRaw(cmd);
                    return "OK\t" + reply;
                }

                if (line.StartsWith("BLIND\t"))
                {
                    string cmd = line.Substring(6);
                    if (!_mount.IsOpen) return "ERR\tmount not connected";
                    _mount.SendBlindRaw(cmd);
                    return "OK";
                }

                // Driver -> hub limit notification. Driver-issued (NINA) slews
                // hit Telescope.SlewError() with rc=1/2/6 but never reach the
                // hub UI's sticky-warn LED otherwise. Forward to MountSession
                // so HubForm's existing LimitWarning handler picks it up.
                if (line.StartsWith("IPC:LIMIT\t"))
                {
                    string reason = line.Substring("IPC:LIMIT\t".Length);
                    DebugLogger.Log("IPC", "received IPC:LIMIT reason='" + reason + "'");
                    try { _mount.RaiseLimitWarning(reason); }
                    catch (Exception ex) { DebugLogger.LogException("IPC", ex); }
                    return "OK";
                }

                DebugLogger.Log("IPC", "received UNKNOWN line='" + (line ?? "") + "'");
                return "ERR\tunknown command";
            }
            catch (Exception ex)
            {
                string m = ex.Message ?? "";
                m = m.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
                DebugLogger.Log("IPC", "Dispatch threw on line='" + (line ?? "") + "': " + m);
                return "ERR\t" + m;
            }
        }
    }
}
