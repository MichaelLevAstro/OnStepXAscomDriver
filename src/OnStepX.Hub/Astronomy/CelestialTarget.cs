using System;

namespace ASCOM.OnStepX.Astronomy
{
    internal sealed class CelestialTarget
    {
        public string Id { get; set; }          // e.g. "M31", "Mars"
        public string Name { get; set; }        // e.g. "Andromeda Galaxy"
        public string Kind { get; set; }        // "Planet", "Galaxy", "Globular", ...
        public string Constellation { get; set; }

        // Solar-system identity for targets whose position must be computed at
        // slew time and whose tracking rate differs from sidereal. null for
        // deep-sky / catalog objects (M, NGC, IC, SH2, LDN).
        public Body? SolarSystemBody { get; set; }

        // Produces (RA hours, Dec degrees) at given UTC instant.
        public Func<DateTime, ValueTuple<double, double>> Coords;
    }
}
