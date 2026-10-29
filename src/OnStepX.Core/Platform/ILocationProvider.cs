using System.Threading.Tasks;

namespace ASCOM.OnStepX.Platform
{
    // Result of asking the machine where it is. Elevation is metres and may be
    // NaN even on success — GPS altitude is often absent.
    public sealed class LocationFix
    {
        public bool Ok;
        public double LatitudeDeg;
        public double LongitudeDeg;
        public double ElevationM;

        // Shown to the user when Ok is false. The Windows implementation
        // distinguishes "location service is off" from "no fix in time",
        // because the first one the user can do something about.
        public string Failure;
    }

    // Backs the site card's "sync from PC". Was
    // System.Device.Location.GeoCoordinateWatcher, which is .NET Framework on
    // Windows only; Linux and macOS have no equivalent worth the trouble, so
    // their implementation reports a failure the UI already knows how to show.
    public interface ILocationProvider
    {
        Task<LocationFix> GetAsync();
    }
}
