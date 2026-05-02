# OnStepX ASCOM + Hub

## Disclaimer
I have tested this with my setup and it works well, but that doesn't mean it'll work well for you.
If it doesn't, but you like the driver and would like it to, open up an issue stating the bug with as much context as possible, and i'll get on it!

Windows ASCOM Telescope driver + mediator app for OnStepX mounts. The driver is an in-process ASCOM shim; the hub is a standalone WinForms app that owns the serial/TCP link and serves many ASCOM clients concurrently over a named pipe.

## Important note
Currently there is no focuser, rotator or weather support, since i dont use them.
But if demand will be high, i will implement.

<img width="1226" height="1381" alt="image" src="https://github.com/user-attachments/assets/4226c6a1-b45b-4b3e-9d4e-f42e935a9360" />
<img width="1225" height="1379" alt="image" src="https://github.com/user-attachments/assets/2b52c40b-ed8e-461e-a55c-26e7be4ff6a0" />
<img width="965" height="745" alt="image" src="https://github.com/user-attachments/assets/bbfb1c6b-22d9-495d-9871-19682078094a" />
<img width="967" height="642" alt="image" src="https://github.com/user-attachments/assets/87020df3-98e4-4f49-9eae-6512e3d65a4a" />

# Features
- Manual slewing
- Set parameters on the fly
- Console
- Sites: Save site locations in the app (not saved in mount due to 3 sites limitation)
- GOTO: Common catalogs added (Planets, Messier, NGC etc...)
- 3D Visualizer

# Install
Go to Install folder, and run installer, done :)

# For Developers
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
                 │   OnStepX.Hub   │   (standalone WPF exe, owns mount)
                 └────────┬────────┘
                          │ Serial / TCP
                          ▼
                     OnStepX MCU
```

Three projects, all .NET Framework 4.8:

- **OnStepX.Shared** — `ITransport` + `PipeTransport` (driver-side client), `LX200Protocol` facade, `CoordFormat` parsers, `TransportLogger`, `DebugLogger`. Linked into both driver and hub so the wire format is defined in exactly one place.
- **OnStepX.Driver** — `ASCOM.OnStepX.Telescope.dll`, in-proc COM (InprocServer32). `Telescope : ITelescopeV3` holds one `PipeTransport` per client. `HubLauncher` auto-spawns `OnStepX.Hub.exe --tray` on first connect when no hub is running, with exponential-backoff retry on the connect handshake.
- **OnStepX.Hub** — `OnStepX.Hub.exe`. WPF + MVVM standalone app with single-instance mutex and tray icon. Owns the only physical mount transport via a process-wide `MountSession` singleton plus `MountStateCache` (polls `:GU#` / `:GR#` / `:GD#` / `:GA#` / `:GZ#` / `:GS#` / `:Gm#` / `:GX42#` / `:GX43#` at ~750 ms). Hosts `HubPipeServer` accepting many concurrent driver clients.

## Hub UI

WPF + MVVM. Shell is [src/OnStepX.Hub/Views/MainWindow.xaml](src/OnStepX.Hub/Views/MainWindow.xaml); each section has its own `ViewModel` under `ViewModels/`.

- **Connection** — Serial / TCP transports, port auto-detect, reconnect.
- **Site / Date-Time** — PC-sync, plus a multi-site manager ([SitesWindow.xaml](src/OnStepX.Hub/Views/SitesWindow.xaml)) that bypasses OnStepX's 3-site firmware limit by storing extras locally.
- **Tracking / Slew** — Tracking enable + rate, slew speed, guide rate, meridian-flip action.
- **Limits / Position** — Horizon and overhead limits with pulsing-LED warning badges; live RA/Dec/Alt/Az/LST/pier-side readout.
- **Console** — Manual LX200 entry + transport log ([ConsoleView](src/OnStepX.Hub/Controls/ConsoleView.xaml)).
- **Slew Pad** — Manual N/S/E/W jogging at the selected slew rate.
- **3D Mount Visualizer** — HelixToolkit.Wpf model showing real-time mount orientation ([MountVisualizer](src/OnStepX.Hub/Controls/MountVisualizer.xaml)).
- **Catalogs / GOTO** — `Astronomy/`: Messier, NGC, IC, Sh2, LDN (loaded from embedded resources under `Astronomy/Data/`) and planets with live ephemeris ([PlanetEphemeris.cs](src/OnStepX.Hub/Astronomy/PlanetEphemeris.cs)). Target picker in [SlewTargetWindow.xaml](src/OnStepX.Hub/Views/SlewTargetWindow.xaml).
- **Advanced Settings** — Firmware parameters and diagnostics tabs ([AdvancedSettingsWindow.xaml](src/OnStepX.Hub/Views/AdvancedSettingsWindow.xaml)); NV reset lives here.
- **Notifications** — Windows toasts via `Notifications/NotificationService.cs` (Microsoft.Toolkit.Uwp.Notifications).
- **Theme** — Dark / light toggle (`Services/ThemeService.cs`), live without restart.

## Wire protocol

Tab-delimited envelope carrying raw LX200 strings. No per-command opcodes — new mount commands ride the existing `CMD\t` / `BLIND\t` envelope without protocol changes. Out-of-band IPC verbs: `IPC:ISCONNECTED` (mount-link health probe), `IPC:SHOWHUB` (pop hub window), and `IPC:LIMIT\t<reason>` (driver → hub when a NINA-issued slew is firmware-rejected for a limit, so the hub flashes the same warning LED its in-app slew form uses). Full spec: [src/OnStepX.Shared/Hardware/Transport/PipeProtocol.md](src/OnStepX.Shared/Hardware/Transport/PipeProtocol.md).

## Build

Requirements: Visual Studio 2022 or .NET SDK 6+, .NET Framework 4.8 Dev Pack, ASCOM Platform 6.6+ installed on the dev machine (driver's GAC references require the `ASCOM.DeviceInterfaces` and `ASCOM.Exceptions` v6 DLLs).

```
dotnet build OnStepX.sln -c Release
```

Artifacts:

- `src/OnStepX.Driver/bin/Release/ASCOM.OnStepX.Telescope.dll`
- `src/OnStepX.Hub/bin/Release/OnStepX.Hub.exe` (+ `HelixToolkit.Wpf.dll`, `Microsoft.Toolkit.Uwp.Notifications.dll`)
- `src/OnStepX.Shared/bin/Release/OnStepX.Shared.dll`

## Register (dev, one-time, admin)

The installer writes CLSID keys directly; regasm is for developer round-trips.

```
installer\register.cmd
```

Driver appears in the ASCOM Chooser as **OnStepX Telescope Driver** (`ASCOM.OnStepX.Telescope`).

## Unregister

```
installer\unregister.cmd
```

## Build installer

Requires Inno Setup 6.

```
build-installer.cmd 0.4.0
```

Args are order-independent and accept `Debug` / `Release`; with `Debug` the ISCC step is skipped. Output: `installer\OnStepX-Setup-0.4.0.exe`. Writes the InprocServer32 keys + `HKLM\SOFTWARE\OnStepX\Hub\InstallPath` consumed by the driver's auto-launcher.

## Running

1. Launch **OnStepX Hub** from the Start menu. Connect to your mount (Auto-Detect, or select COM/baud, or TCP host).
2. In NINA / SGP / etc., pick **OnStepX Telescope Driver** from the Chooser and hit Connect. The driver auto-launches the hub in tray mode if it isn't already running.
3. The hub stays running after clients disconnect — useful as a standalone tool (catalogs, slew pad, manual LX200 console, 3D visualizer). Exit from the tray menu.
