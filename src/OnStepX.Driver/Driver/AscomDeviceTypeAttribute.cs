using System;

namespace ASCOM.OnStepX.Driver
{
    // Marks an ASCOM driver class with the ASCOM Profile DeviceType bucket
    // ("Telescope", "Focuser", "Rotator", …) so the shared [ComRegisterFunction]
    // in DriverRegistration can register multiple driver classes from a single
    // assembly without hard-coding a single device type.
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    internal sealed class AscomDeviceTypeAttribute : Attribute
    {
        public string DeviceType { get; }
        public AscomDeviceTypeAttribute(string deviceType) { DeviceType = deviceType; }
    }
}
