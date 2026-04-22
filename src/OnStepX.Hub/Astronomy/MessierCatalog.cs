using System.Collections.Generic;

namespace ASCOM.OnStepX.Astronomy
{
    // Messier catalog — J2000.0 equatorial coordinates.
    // Source: SEDS Messier database; values rounded to ~arcsec precision.
    internal static class MessierCatalog
    {
        private static readonly IReadOnlyList<CelestialTarget> _all;
        public static IReadOnlyList<CelestialTarget> All => _all;

        static MessierCatalog() { _all = Build(); }

        private static IReadOnlyList<CelestialTarget> Build()
        {
            var e = _entries;
            var list = new List<CelestialTarget>(e.Length);
            for (int k = 0; k < e.Length; k++)
            {
                var r = e[k];
                double raH = r.raH + r.raM / 60.0 + r.raS / 3600.0;
                double decD = r.decD + r.decM / 60.0 + r.decS / 3600.0;
                if (r.decSign < 0) decD = -decD;
                double raConst = raH;
                double decConst = decD;
                list.Add(new CelestialTarget
                {
                    Id = "M" + r.num,
                    Name = r.name.Length == 0 ? "M" + r.num : "M" + r.num + " — " + r.name,
                    Kind = r.kind,
                    Constellation = r.cons,
                    Coords = _ => (raConst, decConst),
                });
            }
            return list;
        }

        private struct Row
        {
            public int num;
            public string name;
            public string kind;
            public string cons;
            public int raH, raM; public double raS;
            public int decSign, decD, decM; public double decS;
            public Row(int n, string nm, string k, string c, int rh, int rm, double rs, int ds, int dd, int dm, double dss)
            { num=n; name=nm; kind=k; cons=c; raH=rh; raM=rm; raS=rs; decSign=ds; decD=dd; decM=dm; decS=dss; }
        }

        private static readonly Row[] _entries = new[]
        {
            new Row(1,  "Crab Nebula",       "SNR",       "Tau",  5, 34, 31.94, +1, 22,  0, 52.2),
            new Row(2,  "",                  "Globular",  "Aqr", 21, 33, 27.02, -1,  0, 49, 23.7),
            new Row(3,  "",                  "Globular",  "CVn", 13, 42, 11.62, +1, 28, 22, 38.2),
            new Row(4,  "",                  "Globular",  "Sco", 16, 23, 35.22, -1, 26, 31, 32.7),
            new Row(5,  "",                  "Globular",  "Ser", 15, 18, 33.22, +1,  2,  4, 51.7),
            new Row(6,  "Butterfly Cluster", "OpenCluster","Sco",17, 40, 20.0,  -1, 32, 15,  0.0),
            new Row(7,  "Ptolemy Cluster",   "OpenCluster","Sco",17, 53, 51.0,  -1, 34, 47,  0.0),
            new Row(8,  "Lagoon Nebula",     "Nebula",    "Sgr", 18,  3, 48.0,  -1, 24, 23,  0.0),
            new Row(9,  "",                  "Globular",  "Oph", 17, 19, 11.78, -1, 18, 30, 58.5),
            new Row(10, "",                  "Globular",  "Oph", 16, 57,  8.92, -1,  4,  5, 58.0),
            new Row(11, "Wild Duck Cluster", "OpenCluster","Sct",18, 51,  5.0,  -1,  6, 16,  0.0),
            new Row(12, "",                  "Globular",  "Oph", 16, 47, 14.18, -1,  1, 56, 54.7),
            new Row(13, "Hercules Cluster",  "Globular",  "Her", 16, 41, 41.24, +1, 36, 27, 35.5),
            new Row(14, "",                  "Globular",  "Oph", 17, 37, 36.15, -1,  3, 14, 45.3),
            new Row(15, "",                  "Globular",  "Peg", 21, 29, 58.33, +1, 12, 10,  1.2),
            new Row(16, "Eagle Nebula",      "Nebula",    "Ser", 18, 18, 48.0,  -1, 13, 49,  0.0),
            new Row(17, "Omega Nebula",      "Nebula",    "Sgr", 18, 20, 26.0,  -1, 16, 10, 36.0),
            new Row(18, "",                  "OpenCluster","Sgr",18, 19, 58.0,  -1, 17,  6,  0.0),
            new Row(19, "",                  "Globular",  "Oph", 17,  2, 37.69, -1, 26, 16,  4.6),
            new Row(20, "Trifid Nebula",     "Nebula",    "Sgr", 18,  2, 23.0,  -1, 23,  1, 48.0),
            new Row(21, "",                  "OpenCluster","Sgr",18,  4, 13.0,  -1, 22, 29,  0.0),
            new Row(22, "Sagittarius Cluster","Globular", "Sgr", 18, 36, 23.94, -1, 23, 54, 17.1),
            new Row(23, "",                  "OpenCluster","Sgr",17, 56, 48.0,  -1, 19,  1,  0.0),
            new Row(24, "Sagittarius Star Cloud","StarCloud","Sgr",18,16,54.0,  -1, 18, 39,  0.0),
            new Row(25, "",                  "OpenCluster","Sgr",18, 31, 47.0,  -1, 19,  7,  0.0),
            new Row(26, "",                  "OpenCluster","Sct",18, 45, 18.0,  -1,  9, 23,  0.0),
            new Row(27, "Dumbbell Nebula",   "PlanetaryNebula","Vul",19,59,36.34,+1,22,43,16.0),
            new Row(28, "",                  "Globular",  "Sgr", 18, 24, 32.89, -1, 24, 52, 11.4),
            new Row(29, "",                  "OpenCluster","Cyg",20, 23, 56.0,  +1, 38, 31, 24.0),
            new Row(30, "",                  "Globular",  "Cap", 21, 40, 22.12, -1, 23, 10, 47.5),
            new Row(31, "Andromeda Galaxy",  "Galaxy",    "And",  0, 42, 44.3,  +1, 41, 16,  9.0),
            new Row(32, "",                  "Galaxy",    "And",  0, 42, 41.8,  +1, 40, 51, 55.0),
            new Row(33, "Triangulum Galaxy", "Galaxy",    "Tri",  1, 33, 50.02, +1, 30, 39, 36.7),
            new Row(34, "",                  "OpenCluster","Per", 2, 42,  5.0,  +1, 42, 45, 42.0),
            new Row(35, "",                  "OpenCluster","Gem", 6,  9,  0.0,  +1, 24, 21,  0.0),
            new Row(36, "",                  "OpenCluster","Aur", 5, 36, 12.0,  +1, 34,  8,  4.0),
            new Row(37, "",                  "OpenCluster","Aur", 5, 52, 18.0,  +1, 32, 33,  2.0),
            new Row(38, "",                  "OpenCluster","Aur", 5, 28, 42.0,  +1, 35, 51, 18.0),
            new Row(39, "",                  "OpenCluster","Cyg",21, 32, 12.0,  +1, 48, 26,  0.0),
            new Row(40, "Winnecke 4",        "DoubleStar","UMa", 12, 22, 12.5,  +1, 58,  4, 59.0),
            new Row(41, "",                  "OpenCluster","CMa", 6, 46,  1.0,  -1, 20, 45, 24.0),
            new Row(42, "Orion Nebula",      "Nebula",    "Ori",  5, 35, 17.3,  -1,  5, 23, 28.0),
            new Row(43, "De Mairan's Nebula","Nebula",    "Ori",  5, 35, 31.0,  -1,  5, 16, 12.0),
            new Row(44, "Beehive Cluster",   "OpenCluster","Cnc", 8, 40, 24.0,  +1, 19, 40,  0.0),
            new Row(45, "Pleiades",          "OpenCluster","Tau", 3, 47, 24.0,  +1, 24,  7,  0.0),
            new Row(46, "",                  "OpenCluster","Pup", 7, 41, 46.0,  -1, 14, 48, 36.0),
            new Row(47, "",                  "OpenCluster","Pup", 7, 36, 35.0,  -1, 14, 29,  0.0),
            new Row(48, "",                  "OpenCluster","Hya", 8, 13, 43.0,  -1,  5, 45,  0.0),
            new Row(49, "",                  "Galaxy",    "Vir", 12, 29, 46.7,  +1,  8,  0,  2.0),
            new Row(50, "",                  "OpenCluster","Mon", 7,  3, 12.0,  -1,  8, 20,  0.0),
            new Row(51, "Whirlpool Galaxy",  "Galaxy",    "CVn", 13, 29, 52.7,  +1, 47, 11, 43.0),
            new Row(52, "",                  "OpenCluster","Cas",23, 24, 48.0,  +1, 61, 35, 36.0),
            new Row(53, "",                  "Globular",  "Com", 13, 12, 55.25, +1, 18, 10,  9.0),
            new Row(54, "",                  "Globular",  "Sgr", 18, 55,  3.33, -1, 30, 28, 47.5),
            new Row(55, "",                  "Globular",  "Sgr", 19, 39, 59.71, -1, 30, 57, 53.1),
            new Row(56, "",                  "Globular",  "Lyr", 19, 16, 35.57, +1, 30, 11,  4.2),
            new Row(57, "Ring Nebula",       "PlanetaryNebula","Lyr",18,53,35.08,+1,33,1,45.0),
            new Row(58, "",                  "Galaxy",    "Vir", 12, 37, 43.5,  +1, 11, 49,  5.0),
            new Row(59, "",                  "Galaxy",    "Vir", 12, 42,  2.3,  +1, 11, 38, 49.0),
            new Row(60, "",                  "Galaxy",    "Vir", 12, 43, 39.6,  +1, 11, 33,  9.0),
            new Row(61, "",                  "Galaxy",    "Vir", 12, 21, 54.9,  +1,  4, 28, 26.0),
            new Row(62, "",                  "Globular",  "Oph", 17,  1, 12.60, -1, 30,  6, 44.5),
            new Row(63, "Sunflower Galaxy",  "Galaxy",    "CVn", 13, 15, 49.3,  +1, 42,  1, 45.0),
            new Row(64, "Black Eye Galaxy",  "Galaxy",    "Com", 12, 56, 43.7,  +1, 21, 40, 58.0),
            new Row(65, "",                  "Galaxy",    "Leo", 11, 18, 55.9,  +1, 13,  5, 32.0),
            new Row(66, "",                  "Galaxy",    "Leo", 11, 20, 15.0,  +1, 12, 59, 30.0),
            new Row(67, "",                  "OpenCluster","Cnc", 8, 51, 24.0,  +1, 11, 49,  0.0),
            new Row(68, "",                  "Globular",  "Hya", 12, 39, 27.98, -1, 26, 44, 38.6),
            new Row(69, "",                  "Globular",  "Sgr", 18, 31, 23.10, -1, 32, 20, 53.1),
            new Row(70, "",                  "Globular",  "Sgr", 18, 43, 12.76, -1, 32, 17, 31.6),
            new Row(71, "",                  "Globular",  "Sge", 19, 53, 46.49, +1, 18, 46, 45.1),
            new Row(72, "",                  "Globular",  "Aqr", 20, 53, 27.70, -1, 12, 32, 14.3),
            new Row(73, "",                  "Asterism",  "Aqr", 20, 58, 54.0,  -1, 12, 38,  0.0),
            new Row(74, "",                  "Galaxy",    "Psc",  1, 36, 41.8,  +1, 15, 47,  1.0),
            new Row(75, "",                  "Globular",  "Sgr", 20,  6,  4.75, -1, 21, 55, 16.2),
            new Row(76, "Little Dumbbell",   "PlanetaryNebula","Per",1,42,24.0,+1,51,34,31.0),
            new Row(77, "",                  "Galaxy",    "Cet",  2, 42, 40.7,  -1,  0,  0, 48.0),
            new Row(78, "",                  "Nebula",    "Ori",  5, 46, 46.0,  +1,  0,  0, 50.0),
            new Row(79, "",                  "Globular",  "Lep",  5, 24, 10.59, -1, 24, 31, 27.3),
            new Row(80, "",                  "Globular",  "Sco", 16, 17,  2.41, -1, 22, 58, 33.9),
            new Row(81, "Bode's Galaxy",     "Galaxy",    "UMa",  9, 55, 33.2,  +1, 69,  3, 55.0),
            new Row(82, "Cigar Galaxy",      "Galaxy",    "UMa",  9, 55, 52.7,  +1, 69, 40, 46.0),
            new Row(83, "Southern Pinwheel", "Galaxy",    "Hya", 13, 37,  0.9,  -1, 29, 51, 57.0),
            new Row(84, "",                  "Galaxy",    "Vir", 12, 25,  3.8,  +1, 12, 53, 13.0),
            new Row(85, "",                  "Galaxy",    "Com", 12, 25, 24.1,  +1, 18, 11, 28.0),
            new Row(86, "",                  "Galaxy",    "Vir", 12, 26, 11.7,  +1, 12, 56, 46.0),
            new Row(87, "Virgo A",           "Galaxy",    "Vir", 12, 30, 49.4,  +1, 12, 23, 28.0),
            new Row(88, "",                  "Galaxy",    "Com", 12, 31, 59.2,  +1, 14, 25, 13.0),
            new Row(89, "",                  "Galaxy",    "Vir", 12, 35, 39.8,  +1, 12, 33, 23.0),
            new Row(90, "",                  "Galaxy",    "Vir", 12, 36, 49.8,  +1, 13,  9, 46.0),
            new Row(91, "",                  "Galaxy",    "Com", 12, 35, 26.4,  +1, 14, 29, 47.0),
            new Row(92, "",                  "Globular",  "Her", 17, 17,  7.39, +1, 43,  8,  9.4),
            new Row(93, "",                  "OpenCluster","Pup", 7, 44, 30.0,  -1, 23, 51, 24.0),
            new Row(94, "",                  "Galaxy",    "CVn", 12, 50, 53.0,  +1, 41,  7, 14.0),
            new Row(95, "",                  "Galaxy",    "Leo", 10, 43, 57.7,  +1, 11, 42, 13.0),
            new Row(96, "",                  "Galaxy",    "Leo", 10, 46, 45.7,  +1, 11, 49, 12.0),
            new Row(97, "Owl Nebula",        "PlanetaryNebula","UMa",11,14,47.7,+1,55,1,9.0),
            new Row(98, "",                  "Galaxy",    "Com", 12, 13, 48.3,  +1, 14, 54,  1.0),
            new Row(99, "",                  "Galaxy",    "Com", 12, 18, 49.6,  +1, 14, 24, 59.0),
            new Row(100,"",                  "Galaxy",    "Com", 12, 22, 54.9,  +1, 15, 49, 21.0),
            new Row(101,"Pinwheel Galaxy",   "Galaxy",    "UMa", 14,  3, 12.6,  +1, 54, 20, 57.0),
            new Row(102,"Spindle Galaxy",    "Galaxy",    "Dra", 15,  6, 29.5,  +1, 55, 45, 48.0),
            new Row(103,"",                  "OpenCluster","Cas", 1, 33, 22.0,  +1, 60, 39,  0.0),
            new Row(104,"Sombrero Galaxy",   "Galaxy",    "Vir", 12, 39, 59.4,  -1, 11, 37, 23.0),
            new Row(105,"",                  "Galaxy",    "Leo", 10, 47, 49.6,  +1, 12, 34, 54.0),
            new Row(106,"",                  "Galaxy",    "CVn", 12, 18, 57.5,  +1, 47, 18, 14.0),
            new Row(107,"",                  "Globular",  "Oph", 16, 32, 31.86, -1, 13,  3, 13.6),
            new Row(108,"",                  "Galaxy",    "UMa", 11, 11, 31.0,  +1, 55, 40, 27.0),
            new Row(109,"",                  "Galaxy",    "UMa", 11, 57, 36.0,  +1, 53, 22, 28.0),
            new Row(110,"",                  "Galaxy",    "And",  0, 40, 22.1,  +1, 41, 41,  7.0),
        };
    }
}
