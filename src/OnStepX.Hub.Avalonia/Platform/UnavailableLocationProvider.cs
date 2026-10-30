using System.Threading.Tasks;
using ASCOM.OnStepX.Platform;

namespace ASCOM.OnStepX.Hub.Platform
{
    // The WPF hub gets a fix from System.Device.Location.GeoCoordinateWatcher,
    // which is .NET Framework only — there is no such assembly on .NET 10, and
    // Linux has no equivalent worth the trouble on a mount computer that is
    // usually a fixed observatory anyway.
    //
    // Reports the failure the site card already knows how to show, pointing at
    // what the user can do instead.
    internal sealed class UnavailableLocationProvider : ILocationProvider
    {
        public Task<LocationFix> GetAsync() => Task.FromResult(new LocationFix
        {
            Ok = false,
            Failure = "This build cannot read a location from the computer.\r\n" +
                      "Type the site in directly, or pick a saved one from Sites.",
        });
    }
}
