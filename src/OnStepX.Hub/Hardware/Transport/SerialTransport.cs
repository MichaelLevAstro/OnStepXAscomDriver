using System;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace ASCOM.OnStepX.Hardware.Transport
{
    internal sealed class SerialTransport : ITransport
    {
        private readonly string _portName;
        private readonly int _baud;
        private SerialPort _port;
        private readonly object _lock = new object();

        public SerialTransport(string portName, int baud = 9600)
        {
            _portName = portName;
            _baud = baud;
        }

        public bool Connected => _port != null && _port.IsOpen;
        public string DisplayName => _portName + "@" + _baud;
        public int TimeoutMs { get; set; } = 1500;

        public void Open()
        {
            lock (_lock)
            {
                if (Connected) return;
                _port = new SerialPort(_portName, _baud, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = TimeoutMs,
                    WriteTimeout = TimeoutMs,
                    // ESP32 and many Arduino boards interpret DTR/RTS toggles as reset lines.
                    // OnStepX does not require them; leaving them asserted reboots the mount on
                    // every connect and the first commands read back bootloader garbage.
                    DtrEnable = false,
                    RtsEnable = false,
                    NewLine = "#"
                };
                _port.Open();
                // Even with DtrEnable/RtsEnable = false, most USB-serial bridges (CH340/CP210x/
                // FTDI) pulse DTR at the moment Windows opens the port, which briefly triggers
                // the ESP32 auto-reset circuit. The MCU then boots ROM loader output at 74880
                // baud (read back as high-byte garbage like \xDF\x98\xCC\xFF at 9600/115200),
                // followed by the application. 1500ms covers ROM boot; the drain loop then
                // waits for the line to go quiet for 150ms before we trust a response.
                Thread.Sleep(1500);
                DrainUntilQuiet(150, 2000);
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                if (_port == null) return;
                try { _port.Close(); } catch { }
                _port.Dispose();
                _port = null;
            }
        }

        public void Dispose() => Close();

        public string SendAndReceive(string command)
        {
            lock (_lock)
            {
                if (!Connected) throw new InvalidOperationException("Serial port not open");
                _port.ReadTimeout = TimeoutMs;
                _port.DiscardInBuffer();
                TransportLogger.Tx(command);
                int start = Environment.TickCount;
                _port.Write(command);
                var reply = ReadUntilHash();
                int elapsed = Environment.TickCount - start;
                TransportLogger.Rx(reply, elapsed);
                TransportLogger.PairEvent(command, reply, elapsed);
                return reply;
            }
        }

        // Read any pending bytes until the line has been silent for `quietMs`, or `maxMs`
        // elapses overall. Used right after Open() to absorb ESP32 boot chatter.
        public void DrainUntilQuiet(int quietMs, int maxMs)
        {
            lock (_lock)
            {
                if (!Connected) return;
                int overallDeadline = Environment.TickCount + maxMs;
                int quietDeadline = Environment.TickCount + quietMs;
                var sb = new StringBuilder();
                while (Environment.TickCount < overallDeadline)
                {
                    if (_port.BytesToRead > 0)
                    {
                        try { sb.Append((char)_port.ReadByte()); } catch { break; }
                        quietDeadline = Environment.TickCount + quietMs;
                    }
                    else
                    {
                        if (Environment.TickCount >= quietDeadline) break;
                        Thread.Sleep(20);
                    }
                }
                if (sb.Length > 0) TransportLogger.Note("boot drain: " + sb.ToString().Replace('\r', ' ').Replace('\n', ' '));
                try { _port.DiscardInBuffer(); } catch { }
            }
        }

        public void SendBlind(string command)
        {
            lock (_lock)
            {
                if (!Connected) throw new InvalidOperationException("Serial port not open");
                TransportLogger.Tx(command);
                _port.Write(command);
                TransportLogger.BlindEvent(command);
            }
        }

        // Read LX200 reply, tolerating async chatter from the mount firmware (notably
        // ESP-IDF log lines like "E (4679) ledc: ... \n" that arrive out-of-band). Any
        // line that ends with CR/LF before we see a terminating '#' is treated as noise
        // and discarded, so a reply split by an interleaved log line still parses.
        private string ReadUntilHash()
        {
            var sb = new StringBuilder();
            int deadline = Environment.TickCount + TimeoutMs;
            while (Environment.TickCount < deadline)
            {
                try
                {
                    int b = _port.ReadByte();
                    if (b < 0) continue;
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
                    // Some commands return a single-char "0" / "1" without '#'.
                    if (sb.Length == 1 && (c == '0' || c == '1'))
                    {
                        Thread.Sleep(10);
                        if (_port.BytesToRead == 0) return sb.ToString();
                    }
                }
                catch (TimeoutException) { break; }
            }
            return sb.ToString();
        }
    }
}
