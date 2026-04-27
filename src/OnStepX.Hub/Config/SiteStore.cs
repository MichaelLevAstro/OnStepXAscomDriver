using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace ASCOM.OnStepX.Config
{
    // User-defined site list persisted as JSON in %APPDATA%\OnStepX\sites.json.
    // Lives alongside — not inside — registry DriverSettings because the list
    // must be portable across machines via import/export. Longitudes stored
    // east-positive (ASCOM/civil convention — LX200Protocol negates at the wire
    // for OnStepX's Meade :Sg/:Gg west-positive convention). One-shot migration
    // from the previous west-positive on-disk format runs in DriverSettings on
    // the first connect after upgrade — see LongitudeConventionVersion.
    [DataContract]
    internal sealed class Site
    {
        [DataMember(Order = 0)] public string Name { get; set; }
        [DataMember(Order = 1)] public double Latitude { get; set; }
        [DataMember(Order = 2)] public double Longitude { get; set; }
        [DataMember(Order = 3)] public double Elevation { get; set; }
    }

    [DataContract]
    internal sealed class SitesFile
    {
        [DataMember(Order = 0)] public int Version { get; set; } = 1;
        [DataMember(Order = 1)] public List<Site> Sites { get; set; } = new List<Site>();
    }

    internal static class SiteStore
    {
        private static string StorePath
        {
            get
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OnStepX");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "sites.json");
            }
        }

        public static List<Site> Load()
        {
            try
            {
                if (!File.Exists(StorePath)) return new List<Site>();
                var bytes = File.ReadAllBytes(StorePath);
                if (bytes.Length == 0) return new List<Site>();
                return Deserialize(bytes)?.Sites ?? new List<Site>();
            }
            catch
            {
                return new List<Site>();
            }
        }

        public static void Save(IEnumerable<Site> sites)
        {
            var file = new SitesFile { Sites = sites?.ToList() ?? new List<Site>() };
            File.WriteAllBytes(StorePath, Serialize(file));
        }

        public static void ExportTo(string path, IEnumerable<Site> sites)
        {
            var file = new SitesFile { Sites = sites?.ToList() ?? new List<Site>() };
            File.WriteAllBytes(path, Serialize(file));
        }

        // Import is merge-by-name (overwrite existing, append new) rather than
        // replace so users can pull in a colleague's sites without losing their
        // own list. Returns the merged result; caller decides when to Save().
        public static List<Site> ImportFrom(string path, IEnumerable<Site> existing)
        {
            var bytes = File.ReadAllBytes(path);
            var incoming = Deserialize(bytes)?.Sites ?? new List<Site>();
            var merged = (existing ?? Enumerable.Empty<Site>())
                .ToDictionary(s => s.Name ?? "", s => s, StringComparer.OrdinalIgnoreCase);
            foreach (var s in incoming)
            {
                if (string.IsNullOrWhiteSpace(s.Name)) continue;
                merged[s.Name] = s;
            }
            return merged.Values.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static SitesFile Deserialize(byte[] bytes)
        {
            var ser = new DataContractJsonSerializer(typeof(SitesFile));
            using (var ms = new MemoryStream(bytes))
                return (SitesFile)ser.ReadObject(ms);
        }

        private static byte[] Serialize(SitesFile file)
        {
            var ser = new DataContractJsonSerializer(typeof(SitesFile));
            using (var ms = new MemoryStream())
            {
                // Indented output so the file is human-editable / diff-friendly.
                using (var writer = System.Runtime.Serialization.Json.JsonReaderWriterFactory
                    .CreateJsonWriter(ms, Encoding.UTF8, ownsStream: false, indent: true))
                {
                    ser.WriteObject(writer, file);
                    writer.Flush();
                }
                return ms.ToArray();
            }
        }
    }
}
