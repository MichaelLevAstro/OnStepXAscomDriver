using System;

namespace ASCOM.OnStepX.Astronomy
{
    internal sealed class CelestialTarget
    {
        public string Id { get; set; }          // e.g. "M31", "Mars"
        public string Name { get; set; }        // e.g. "Andromeda Galaxy"
        public string Kind { get; set; }        // "Planet", "Galaxy", "Globular", ...
        public string Constellation { get; set; }

        // Produces (RA hours, Dec degrees) at given UTC instant.
        public Func<DateTime, ValueTuple<double, double>> Coords;
    }
}
