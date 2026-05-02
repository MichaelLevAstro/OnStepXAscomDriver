using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using ASCOM.OnStepX.Hardware.Transport;
using Microsoft.Win32;

namespace ASCOM.OnStepX.Driver
{
    // Spawns the OnStepX.Hub EXE when the driver pipe-connect fails because no
    // hub is running. Client-app use case (NINA, SGP, CdC, Conform) — user
    // clicks Connect in the client, driver auto-lights up the hub with no
    // manual pre-launch.
    //
    // Install path lookup order:
    //   1. HKLM\SOFTWARE\OnStepX\Hub\InstallPath  (installer writes this)
    //   2. %ProgramFiles%\OnStepX\OnStepX.Hub.exe (default install location)
    //   3. Driver DLL directory  (dev/standalone-deploy sanity)
    internal static class HubLauncher
    {
        private const string REG_ROOT = @"SOFTWARE\OnStepX\Hub";
        private const string REG_VALUE = "InstallPath";
        // Optional installer-written value pointing at the new WPF hub. When
        // present the launcher prefers it; falls back to the legacy WinForms
        // exe so a partial install (only legacy hub on disk) still works.
        private const string REG_VALUE_WPF = "WpfInstallPath";
        private const string EXE_WPF = "OnStepX.Hub.Wpf.exe";
        private const string EXE_LEGACY = "OnStepX.Hub.exe";

        // Overall budget split into two phases:
        //   Phase A — pipe availability (hub process up + HubPipeServer listening).
        //             Short budget; if this fails, hub never launched or crashed.
        //   Phase B — mount-online handshake (IPC:ISCONNECTED:TRUE).
        //             Longer budget so user has time to click Connect in the hub
        //             after the driver auto-launches it for the first time.
        public static bool TryEnsureRunning(PipeTransport transport, int overallTimeoutMs = 30000)
        {
            // Optimistic: maybe hub is up AND mount online already.
            if (TryConnect(transport, 500)) return true;

            // Figure out why that failed: pipe unreachable, or pipe up but mount offline?
            bool pipeUp = IsPipeReachable(500);

            if (!pipeUp)
            {
                string hubPath = LocateHub();
                if (hubPath == null)
                    throw new FileNotFoundException(
                        EXE_WPF + " (or " + EXE_LEGACY + ") not found. Reinstall OnStepX or launch the hub manually before connecting.");

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = hubPath,
                        // No --tray: launch visible so the user sees the hub and
                        // knows they need to click Connect if the mount is offline.
                        // Previous --tray default left users thinking nothing happened.
                        Arguments = "",
                        UseShellExecute = false,
                        WorkingDirectory = Path.GetDirectoryName(hubPath) ?? ""
                    };
                    Process.Start(psi);
                }
                catch (Win32Exception ex) when ((uint)ex.NativeErrorCode == 0x3F0) // ERROR_NO_TOKEN
                {
                    throw new InvalidOperationException(
                        "OnStepX.Hub cannot be auto-launched from a non-interactive session. " +
                        "Launch OnStepX.Hub manually on the desktop before starting the ASCOM client.", ex);
                }

                // Phase A: wait up to ~8 s for the pipe server to come up.
                int pipeDeadline = Environment.TickCount + Math.Min(8000, overallTimeoutMs);
                int waitMs = 200;
                while (Environment.TickCount < pipeDeadline && !pipeUp)
                {
                    Thread.Sleep(waitMs);
                    pipeUp = IsPipeReachable(500);
                    waitMs = Math.Min(1000, waitMs * 2);
                }
                if (!pipeUp)
                    throw new TimeoutException(
                        "OnStepX.Hub started but its pipe never became reachable. " +
                        "Check Event Viewer → Application for a hub crash.");
            }

            // Phase B: pipe is up. Handshake loop — wait for the user to click
            // Connect in the hub UI if the mount isn't online yet.
            int deadline = Environment.TickCount + overallTimeoutMs;
            int backoff = 250;
            while (Environment.TickCount < deadline)
            {
                if (TryConnect(transport, 1000)) return true;
                Thread.Sleep(backoff);
                backoff = Math.Min(1500, backoff + 250);
            }
            return false;
        }

        private static bool TryConnect(PipeTransport t, int timeoutMs)
        {
            int prior = t.TimeoutMs;
            t.TimeoutMs = timeoutMs;
            try { t.Open(); return true; }
            catch { return false; }
            finally { t.TimeoutMs = prior; }
        }

        // Cheap pipe-presence probe — connects the named pipe without running
        // the IPC:ISCONNECTED handshake. Used to distinguish "hub not running"
        // from "hub running but mount offline".
        private static bool IsPipeReachable(int timeoutMs)
        {
            try
            {
                using (var p = new System.IO.Pipes.NamedPipeClientStream(
                    ".", PipeTransport.PIPE_NAME, System.IO.Pipes.PipeDirection.InOut))
                {
                    p.Connect(timeoutMs);
                    return p.IsConnected;
                }
            }
            catch { return false; }
        }

        private static string LocateHub()
        {
            // Lookup order at every step: prefer the WPF exe, fall back to the
            // legacy exe. Both share the single-instance mutex, so whichever
            // one is found is interchangeable from the driver's perspective.

            // 1. HKLM registry — installer can write either the new WpfInstallPath
            //    value or the original InstallPath.
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(REG_ROOT))
                {
                    if (k?.GetValue(REG_VALUE_WPF) is string sw && File.Exists(sw)) return sw;
                    if (k?.GetValue(REG_VALUE) is string sl && File.Exists(sl)) return sl;
                }
            }
            catch { }

            // 2. %ProgramFiles%\OnStepX\
            try
            {
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string wpf = Path.Combine(pf, "OnStepX", EXE_WPF);
                if (File.Exists(wpf)) return wpf;
                string legacy = Path.Combine(pf, "OnStepX", EXE_LEGACY);
                if (File.Exists(legacy)) return legacy;
            }
            catch { }

            // 3. Driver DLL directory (dev/standalone-deploy).
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string wpf = Path.Combine(dir, EXE_WPF);
                if (File.Exists(wpf)) return wpf;
                string legacy = Path.Combine(dir, EXE_LEGACY);
                if (File.Exists(legacy)) return legacy;
            }
            catch { }

            return null;
        }
    }
}
