# OnStepX ASCOM + Hub

Windows ASCOM Telescope driver + mediator app for OnStepX mounts. The driver is an in-process ASCOM shim; the hub is a standalone WinForms app that owns the serial/TCP link and serves many ASCOM clients concurrently over a named pipe.

## Architecture (v0.4.0)

```
 NINA / SGP / CdC / Conform       NINA / SGP / CdC / Conform
          │ COM                              │ COM
          ▼                                  ▼
┌────────────────────────┐        ┌────────────────────────┐
│ ASCOM.OnStepX.Telescope│   ...  │ ASCOM.OnStepX.Telescope│   (in-proc DLL per client)
│      (Driver)          │        │      (Driver)          │
└──────────┬─────────────┘        └──────────┬─────────────┘
           │ \\.\pipe\OnStepXHub             │
           └──────────────┬──────────────────┘
                          ▼
                 ┌─────────────────┐
                 │   OnStepX.Hub   │   (standalone WinForms exe, owns mount)
                 └────────┬────────┘
                          │ Serial / TCP
                          ▼
                     OnStepX MCU
```

- **OnStepX.Shared** — LX200 protocol, coord parsers, `ITransport`, `PipeTransport`, `TransportLogger`. Linked into both driver and hub.
- **OnStepX.Driver** — `ASCOM.OnStepX.Telescope.dll`, InprocServer32. `Telescope : ITelescopeV3` holds a `PipeTransport` per client. Auto-launches `OnStepX.Hub.exe --tray` on first connect if no hub is running.
- **OnStepX.Hub** — `OnStepX.Hub.exe`. Persistent app with UI, catalogs (NGC/IC/Sh2/LDN/Messier/planets), slew pad, manual LX200 console, transport log. Runs one `MountSession` + `MountStateCache` + serial/TCP transport. Hosts `HubPipeServer` that accepts many concurrent driver clients.

## Wire protocol

Tab-delimited envelope carrying raw LX200 strings. No per-command opcodes. See [src/OnStepX.Shared/Hardware/Transport/PipeProtocol.md](src/OnStepX.Shared/Hardware/Transport/PipeProtocol.md).

## Build

Requirements: Visual Studio 2022 or .NET SDK 6+, .NET Framework 4.8 Dev Pack, ASCOM Platform 6.6+ installed on the dev machine (driver's GAC references require the ASCOM.DeviceInterfaces and ASCOM.Exceptions v6 DLLs).

```
dotnet build OnStepX.sln -c Release
```

Artifacts:

- `src/OnStepX.Driver/bin/Release/ASCOM.OnStepX.Telescope.dll`
- `src/OnStepX.Hub/bin/Release/OnStepX.Hub.exe`
- `src/OnStepX.Shared/bin/Release/OnStepX.Shared.dll`

## Register (dev, one-time, admin)

The installer writes CLSID keys directly; regasm is for developer round-trips.

```
installer\register.cmd
```

Driver appears in ASCOM Chooser as **OnStepX Telescope Driver** (`ASCOM.OnStepX.Telescope`).

## Unregister

```
installer\unregister.cmd
```

## Build installer

```
build-installer.cmd 0.4.0
```

Output: `installer\OnStepX-Setup-0.4.0.exe`. Writes InprocServer32 keys + `HKLM\SOFTWARE\OnStepX\Hub\InstallPath` (consumed by the driver's auto-launcher), kills any residual v0.3.x LocalServer on upgrade.

## Running

1. Launch **OnStepX Hub** from the Start menu. Connect to your mount (Auto-Detect or select COM/baud or TCP host).
2. In NINA / SGP / etc., pick **OnStepX Telescope Driver** from the Chooser and hit Connect. Driver auto-launches the hub in tray mode if it isn't already running.
3. The hub stays running after clients disconnect — it's a useful standalone tool (catalogs, slew pad, manual LX200 console). Exit from the tray menu.

## Status

v0.4.0 — split from monolithic v0.3.16. Driver + Hub separation, generic pipe envelope, multi-client, auto-launch.
