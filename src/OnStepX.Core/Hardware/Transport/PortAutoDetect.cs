using System;
using System.Collections.Generic;
using System.IO.Ports;

namespace ASCOM.OnStepX.Hardware.Transport
{
    internal static class PortAutoDetect
    {
        public static string[] ListSerialPorts() => SerialPort.GetPortNames();

        // Returns the first COM port that responds to :GVM# with a reply containing "On-Step" or "OnStepX".
        public static string FindOnStepPort(int[] baudsToTry = null, int perPortTimeoutMs = 600)
        {
            baudsToTry = baudsToTry ?? new[] { 9600, 115200, 57600, 38400, 19200 };
            foreach (var name in SerialPort.GetPortNames())
            {
                foreach (var b in baudsToTry)
                {
                    try
                    {
                        using (var t = new SerialTransport(name, b) { TimeoutMs = perPortTimeoutMs })
                        {
                            t.Open();
                            var reply = t.SendAndReceive(":GVP#");
                            if (!string.IsNullOrEmpty(reply) && reply.IndexOf("On", StringComparison.OrdinalIgnoreCase) >= 0)
                                return name;
                        }
                    }
                    catch { /* try next */ }
                }
            }
            return null;
        }
    }
}
