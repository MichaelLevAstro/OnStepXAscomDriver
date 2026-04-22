namespace ASCOM.OnStepX.Driver
{
    internal static class TelescopeCapabilities
    {
        public const bool CanFindHome = true;
        public const bool CanPark = true;
        public const bool CanUnpark = true;
        public const bool CanSetPark = true;
        public const bool CanSetTracking = true;
        public const bool CanSlew = true;
        public const bool CanSlewAsync = true;
        public const bool CanSlewAltAz = true;
        public const bool CanSlewAltAzAsync = true;
        public const bool CanSync = true;
        public const bool CanSyncAltAz = false;
        public const bool CanPulseGuide = true;
        public const bool CanSetGuideRates = true;
        public const bool CanSetDeclinationRate = false;
        public const bool CanSetRightAscensionRate = false;
        public const bool CanSetPierSide = false;
        public const bool CanMoveAxis = true;
        public const bool CanAbortSlew = true;
    }
}
