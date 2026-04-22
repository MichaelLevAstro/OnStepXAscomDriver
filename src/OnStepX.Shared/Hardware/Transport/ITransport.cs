using System;

namespace ASCOM.OnStepX.Hardware.Transport
{
    internal interface ITransport : IDisposable
    {
        bool Connected { get; }
        string DisplayName { get; }
        int TimeoutMs { get; set; }

        void Open();
        void Close();

        // Commands: send, wait for reply terminated by '#'. Thread-safe.
        string SendAndReceive(string command);

        // Fire-and-forget (for motion start/stop). Still serialized by implementation.
        void SendBlind(string command);
    }
}
