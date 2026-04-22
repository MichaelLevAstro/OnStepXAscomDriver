using System;
using System.Threading;

namespace ASCOM.OnStepX.Hardware.State
{
    internal static class ClientRegistry
    {
        private static int _count;
        public static event EventHandler Changed;

        public static int Count => _count;

        public static void Add() { Interlocked.Increment(ref _count); Changed?.Invoke(null, EventArgs.Empty); }
        public static void Remove()
        {
            if (Interlocked.Decrement(ref _count) < 0) Interlocked.Exchange(ref _count, 0);
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
