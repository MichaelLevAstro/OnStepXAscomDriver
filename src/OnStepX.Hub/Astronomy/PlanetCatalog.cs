using System.Collections.Generic;

namespace ASCOM.OnStepX.Astronomy
{
    internal static class PlanetCatalog
    {
        public static IReadOnlyList<CelestialTarget> All { get; } = Build();

        private static IReadOnlyList<CelestialTarget> Build()
        {
            var list = new List<CelestialTarget>();
            Add(list, Body.Sun, "Sun");
            Add(list, Body.Moon, "Moon");
            Add(list, Body.Mercury, "Mercury");
            Add(list, Body.Venus, "Venus");
            Add(list, Body.Mars, "Mars");
            Add(list, Body.Jupiter, "Jupiter");
            Add(list, Body.Saturn, "Saturn");
            Add(list, Body.Uranus, "Uranus");
            Add(list, Body.Neptune, "Neptune");
            return list;
        }

        private static void Add(List<CelestialTarget> list, Body b, string name)
        {
            list.Add(new CelestialTarget
            {
                Id = name,
                Name = name,
                Kind = b == Body.Sun || b == Body.Moon ? b.ToString() : "Planet",
                Constellation = "",
                Coords = utc =>
                {
                    double ra, dec;
                    PlanetEphemeris.Compute(b, utc, out ra, out dec);
                    return (ra, dec);
                }
            });
        }
    }
}
