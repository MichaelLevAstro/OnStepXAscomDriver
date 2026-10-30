using System;
using System.Collections.Generic;
using System.Globalization;
using ASCOM.OnStepX.Diagnostics;
using Microsoft.Win32;

namespace ASCOM.OnStepX.Hardware.Tppa
{
    // Read-only view of the com0com pair ledger written by the installer.
    // Hub never invokes setupc.exe at runtime — that would prompt UAC every
    // call and break the "Hub runs as normal user" contract.
    // Registry: HKLM\SOFTWARE\OnStepX\Hub\Com0comManagedPairs (REG_SZ)
    //           "<pairNum>|<PortA>|<PortB>[;<pairNum>|<PortA>|<PortB>...]"
    internal static class Com0comManager
    {
        public sealed class PairInfo
        {
            public int PairNumber { get; set; }
            public string PortA { get; set; } = "";
            public string PortB { get; set; } = "";
        }

        private const string ManagedPairsRegPath = @"SOFTWARE\OnStepX\Hub";
        private const string ManagedPairsValueName = "Com0comManagedPairs";

        public static IReadOnlyList<PairInfo> GetManagedPairsFromRegistry()
        {
            // com0com is a Windows kernel driver and this ledger is written by
            // the Windows installer, so there is nothing to read anywhere else.
            // Without this the registry call throws on every Linux start and
            // logs an exception for a feature that cannot exist there.
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows))
                return Array.Empty<PairInfo>();

            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(ManagedPairsRegPath))
                {
                    if (k == null) return Array.Empty<PairInfo>();
                    var raw = k.GetValue(ManagedPairsValueName);
                    string[] entries;
                    if (raw is string[] arr) entries = arr;
                    else if (raw is string s) entries = s.Split(';');
                    else return Array.Empty<PairInfo>();

                    var list = new List<PairInfo>();
                    foreach (var e in entries)
                    {
                        if (string.IsNullOrWhiteSpace(e)) continue;
                        var parts = e.Split('|');
                        if (parts.Length < 3) continue;
                        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) continue;
                        list.Add(new PairInfo
                        {
                            PairNumber = n,
                            PortA = parts[1] ?? "",
                            PortB = parts[2] ?? "",
                        });
                    }
                    return list;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogException("COM0COM", ex);
                return Array.Empty<PairInfo>();
            }
        }
    }
}
