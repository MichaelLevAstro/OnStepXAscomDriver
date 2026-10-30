using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace ASCOM.OnStepX.Astronomy
{
    // Loads a tab-separated catalog from an embedded resource.
    // Format per line: ID\tRA_hours\tDec_deg\tKind\tConst\tName
    internal static class DeepSkyCatalog
    {
        private static readonly Dictionary<string, IReadOnlyList<CelestialTarget>> _cache
            = new Dictionary<string, IReadOnlyList<CelestialTarget>>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _gate = new object();

        public static IReadOnlyList<CelestialTarget> Load(string resourceName)
        {
            lock (_gate)
            {
                IReadOnlyList<CelestialTarget> v;
                if (_cache.TryGetValue(resourceName, out v)) return v;
                v = LoadFromResource(resourceName);
                _cache[resourceName] = v;
                return v;
            }
        }

        private static IReadOnlyList<CelestialTarget> LoadFromResource(string resourceName)
        {
            var asm = typeof(DeepSkyCatalog).Assembly;
            string fullName = FindResource(asm, resourceName);
            if (fullName == null)
                throw new FileNotFoundException("Embedded catalog not found: " + resourceName);

            var list = new List<CelestialTarget>(8192);
            using (var stream = asm.GetManifestResourceStream(fullName))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    var parts = line.Split('\t');
                    if (parts.Length < 3) continue;
                    double ra, dec;
                    if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out ra)) continue;
                    if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out dec)) continue;
                    string id = parts[0];
                    string kind = parts.Length > 3 ? parts[3] : "";
                    string cst = parts.Length > 4 ? parts[4] : "";
                    string cmn = parts.Length > 5 ? parts[5] : "";
                    string pretty = PrettyType(kind);
                    double raConst = ra, decConst = dec;
                    list.Add(new CelestialTarget
                    {
                        Id = id,
                        Name = string.IsNullOrEmpty(cmn) ? id : id + " — " + cmn,
                        Kind = pretty,
                        Constellation = cst,
                        Coords = _ => (raConst, decConst),
                    });
                }
            }
            return list;
        }

        private static string FindResource(Assembly asm, string needle)
        {
            foreach (var n in asm.GetManifestResourceNames())
                if (n.EndsWith(needle, StringComparison.OrdinalIgnoreCase)) return n;
            return null;
        }

        private static string PrettyType(string t)
        {
            switch (t)
            {
                case "*":     return "Star";
                case "**":    return "DoubleStar";
                case "*Ass":  return "StellarAssoc";
                case "OCl":   return "OpenCluster";
                case "GCl":   return "Globular";
                case "Cl+N":  return "Cluster+Neb";
                case "G":     return "Galaxy";
                case "GPair": return "GalaxyPair";
                case "GTrpl": return "GalaxyTriplet";
                case "GGroup":return "GalaxyGroup";
                case "PN":    return "PlanetaryNebula";
                case "HII":   return "HII";
                case "DrkN":  return "DarkNebula";
                case "DkN":   return "DarkNebula";
                case "EmN":   return "EmissionNebula";
                case "Neb":   return "Nebula";
                case "RfN":   return "ReflectionNebula";
                case "SNR":   return "SNR";
                case "Nova":  return "Nova";
                case "Other": return "Other";
                default:      return string.IsNullOrEmpty(t) ? "—" : t;
            }
        }
    }
}
