using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ASCOM.OnStepX.Hardware.Transport
{
    internal sealed class TcpTransport : ITransport
    {
        private readonly string _host;
        private readonly int _port;
        private TcpClient _client;
        private NetworkStream _stream;
        private readonly object _lock = new object();

        public TcpTransport(string host, int port = 9999)
        {
            _host = host;
            _port = port;
        }

        public bool Connected => _client != null && _client.Connected;
        public string DisplayName => _host + ":" + _port;
        public int TimeoutMs { get; set; } = 1500;

        public void Open()
        {
            lock (_lock)
            {
                if (Connected) return;
                _client = new TcpClient();
                _client.NoDelay = true;
                var ar = _client.BeginConnect(_host, _port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(TimeoutMs))
                {
                    _client.Close();
                    throw new TimeoutException("TCP connect timed out");
                }
                _client.EndConnect(ar);
                _stream = _client.GetStream();
                _stream.ReadTimeout = TimeoutMs;
                _stream.WriteTimeout = TimeoutMs;
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                try { _stream?.Close(); } catch { }
                try { _client?.Close(); } catch { }
                _stream = null;
                _client = null;
            }
        }

        public void Dispose() => Close();

        public string SendAndReceive(string command)
        {
            lock (_lock)
            {
                if (!Connected) throw new InvalidOperationException("TCP not connected");
                _stream.ReadTimeout = TimeoutMs;
                var buf = Encoding.ASCII.GetBytes(command);
                TransportLogger.Tx(command);
                int start = Environment.TickCount;
                _stream.Write(buf, 0, buf.Length);
                var reply = ReadUntilHash();
                int elapsed = Environment.TickCount - start;
                TransportLogger.Rx(reply, elapsed);
                TransportLogger.PairEvent(command, reply, elapsed);
                return reply;
            }
        }

        public void SendBlind(string command)
        {
            lock (_lock)
            {
                if (!Connected) throw new InvalidOperationException("TCP not connected");
                var buf = Encoding.ASCII.GetBytes(command);
                TransportLogger.Tx(command);
                _stream.Write(buf, 0, buf.Length);
                TransportLogger.BlindEvent(command);
            }
        }

        // See SerialTransport.ReadUntilHash — we discard any line that terminates in
        // CR/LF without a '#' so async firmware log chatter can't corrupt a reply.
        private string ReadUntilHash()
        {
            var sb = new StringBuilder();
            int deadline = Environment.TickCount + TimeoutMs;
            while (Environment.TickCount < deadline)
            {
                try
                {
                    int b = _stream.ReadByte();
                    if (b < 0) break;
                    char c = (char)b;
                    if (c == '\r' || c == '\n')
                    {
                        if (sb.Length > 0)
                        {
                            TransportLogger.Note("noise: " + sb.ToString());
                            sb.Length = 0;
                        }
                        continue;
                    }
                    sb.Append(c);
                    if (c == '#') return sb.ToString();
                    if (sb.Length == 1 && (c == '0' || c == '1'))
                    {
                        Thread.Sleep(10);
                        if (!_stream.DataAvailable) return sb.ToString();
                    }
                }
                catch (System.IO.IOException) { break; }
            }
            return sb.ToString();
        }
    }
}
