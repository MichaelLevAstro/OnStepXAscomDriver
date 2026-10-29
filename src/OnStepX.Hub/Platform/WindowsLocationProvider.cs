using System;
using System.Device.Location;
using System.Threading.Tasks;
using ASCOM.OnStepX.Platform;

namespace ASCOM.OnStepX.Hub.Platform
{
    // GeoCoordinateWatcher, moved out of SiteViewModel unchanged: fifteen
    // seconds to start the service, then up to ten more for an actual fix,
    // because TryStart returning true only means the service is running.
    internal sealed class WindowsLocationProvider : ILocationProvider
    {
        public async Task<LocationFix> GetAsync()
        {
            using (var watcher = new GeoCoordinateWatcher(GeoPositionAccuracy.High))
            {
                watcher.MovementThreshold = 1.0;

                bool started = await Task.Run(() => watcher.TryStart(false, TimeSpan.FromSeconds(15)));
                if (!started)
                {
                    return new LocationFix
                    {
                        Ok = false,
                        Failure = "Windows Location service is unavailable or disabled.\r\n" +
                                  "Enable it in Settings → Privacy & security → Location, and allow desktop apps.",
                    };
                }

                var c = watcher.Position.Location;
                int waited = 0;
                while ((c.IsUnknown || double.IsNaN(c.Latitude)) && waited < 10000)
                {
                    await Task.Delay(250);
                    c = watcher.Position.Location;
                    waited += 250;
                }

                if (c.IsUnknown)
                {
                    return new LocationFix
                    {
                        Ok = false,
                        Failure = "Could not obtain a location fix within 10 seconds.",
                    };
                }

                return new LocationFix
                {
                    Ok = true,
                    LatitudeDeg = c.Latitude,
                    LongitudeDeg = c.Longitude,
                    ElevationM = c.Altitude,
                };
            }
        }
    }
}
