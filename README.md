# OnStepX ASCOM + Hub

Windows ASCOM driver bundle (Telescope + Focuser + Rotator) plus mediator app for OnStepX mounts. Drivers are in-process ASCOM shims that all pipe to a single hub; the hub is a standalone WPF app that owns the serial/TCP link and serves many ASCOM clients concurrently over a named pipe.

## Disclaimer
I have tested this with my setup and it works well, but that doesn't mean it'll work well for you.
If it doesn't, but you like the driver and would like it to, open up an issue stating the bug with as much context as possible, and i'll get on it!

## Important note
Currently there is no switch, auxillary functions or weather support.

<img width="1226" height="1381" alt="image" src="https://github.com/user-attachments/assets/4226c6a1-b45b-4b3e-9d4e-f42e935a9360" />

<img width="1225" height="1379" alt="image" src="https://github.com/user-attachments/assets/2b52c40b-ed8e-461e-a55c-26e7be4ff6a0" />

<img width="965" height="745" alt="image" src="https://github.com/user-attachments/assets/bbfb1c6b-22d9-495d-9871-19682078094a" />

<img width="967" height="642" alt="image" src="https://github.com/user-attachments/assets/87020df3-98e4-4f49-9eae-6512e3d65a4a" />

<img width="522" height="1095" alt="image" src="https://github.com/user-attachments/assets/729ea97a-9a58-4d17-98f3-abca6e4de25d" />

# Features
- Manual slewing
- Set parameters on the fly
- Console
- Sites: Save site locations in the app (not saved in mount due to 3 sites limitation)
- GOTO: Common catalogs added (Planets, Messier, NGC etc...)
- 3D Visualizer
- And more!

# Auto Polar Alignment (Experimental)
Drives a motorized polar alignment wedge through NINA's **Three Point Polar Alignment (TPPA)** plugin without any custom NINA add-on. Tick **Enable Automatic Polar Alignment** in the hub's ADVANCED card and TPPA's **OAPA System** profile finds the wedge automatically.

Uses firmware **AXIS4** as the **Altitude** screw motor and **AXIS5** as the **Azimuth** screw motor. The hub bundles the virtual COM port driver, so installation is one click and the hub never needs admin to run.

Full setup, jog controls, calibration and troubleshooting in [installer/POLAR_ALIGNMENT_README.md](installer/POLAR_ALIGNMENT_README.md).

# Install
Go to Install folder, and run installer, done :)

# For Developers
## Architecture

```
 NINA / SGP / CdC / Conform                NINA / SGP / etc.
   │ COM (Telescope/Focuser/Rotator)        │ COM
   ▼                                        ▼
┌──────────────────────────┐        ┌──────────────────────────┐
│ ASCOM.OnStepX.dll        │  ...   │ ASCOM.OnStepX.dll        │   (in-proc DLL, one
│   ┌────────────────────┐ │        │   ┌────────────────────┐ │    instance per client
│   │ Telescope (V3)     │ │        │   │ Focuser (V2)       │ │    process)
│   │ Focuser  (V2)      │ │        │   │ Rotator  (V3)      │ │
│   │ Rotator  (V3)      │ │        │   │ ...                │ │
│   └────────────────────┘ │        │   └────────────────────┘ │
└────────────┬─────────────┘        └────────────┬─────────────┘
             │ \\.\pipe\OnStepXHub               │
             └──────────────────┬────────────────┘
                                ▼
                       ┌─────────────────┐
                       │   OnStepX.Hub   │   (standalone WPF exe, owns mount)
                       └────────┬────────┘
                                │ Serial / TCP
                                ▼
                           OnStepX MCU
```

One DLL hosts every ASCOM class (Telescope, Focuser, Rotator). Each class has its own COM `[Guid]` / `[ProgId]` and registers as a separate ASCOM device. They all open their own `PipeTransport` to the same hub — the hub serializes wire access to the mount.

Three projects, all .NET Framework 4.8:

- **OnStepX.Shared** — `ITransport` + `PipeTransport` (driver-side client), `LX200Protocol` facade (Telescope + Focuser + Rotator command surface in one file), `CoordFormat` parsers, `TransportLogger`, `DebugLogger`. Linked into both driver and hub so the wire format lives in exactly one place.
- **OnStepX.Driver** — `ASCOM.OnStepX.dll`, in-proc COM (InprocServer32). Three classes share `HubLauncher` (auto-spawns `OnStepX.Hub.exe` on first connect) and `DriverRegistration` (attribute-driven — `[AscomDeviceType("...")]` opts each class into its ASCOM Profile bucket). Each class instance owns one `PipeTransport`.
- **OnStepX.Hub** — `OnStepX.Hub.exe`. WPF + MVVM standalone app with single-instance mutex and tray icon. Owns the only physical mount transport via a process-wide `MountSession` singleton plus `MountStateCache` (polls mount state at ~750 ms; focuser + rotator ride along on a 4× slower cadence). Hosts `HubPipeServer` accepting many concurrent driver clients. Third column collapses when neither focuser nor rotator is configured in firmware.

## Hub UI

WPF + MVVM. Shell is [src/OnStepX.Hub/Views/MainWindow.xaml](src/OnStepX.Hub/Views/MainWindow.xaml); each section has its own `ViewModel` under `ViewModels/`. Layout is three columns; the third hides automatically when no focuser/rotator is detected and the window resizes to fit.

- **Connection** — Serial / TCP transports, port auto-detect, reconnect.
- **Site / Date-Time** — PC-sync, plus a multi-site manager ([SitesWindow.xaml](src/OnStepX.Hub/Views/SitesWindow.xaml)) that bypasses OnStepX's 3-site firmware limit by storing extras locally.
- **Tracking / Slew** — Tracking enable + rate, slew speed, guide rate, meridian-flip action.
- **Limits / Position** — Horizon and overhead limits with pulsing-LED warning badges; live RA/Dec/Alt/Az/LST/pier-side readout.
- **Console** — Manual LX200 entry + transport log ([ConsoleView](src/OnStepX.Hub/Controls/ConsoleView.xaml)).
- **Slew Pad** — Manual N/S/E/W jogging at the selected slew rate.
- **3D Mount Visualizer** — HelixToolkit.Wpf model showing real-time mount orientation ([MountVisualizer](src/OnStepX.Hub/Controls/MountVisualizer.xaml)).
- **Catalogs / GOTO** — `Astronomy/`: Messier, NGC, IC, Sh2, LDN (loaded from embedded resources under `Astronomy/Data/`) and planets with live ephemeris ([PlanetEphemeris.cs](src/OnStepX.Hub/Astronomy/PlanetEphemeris.cs)). Target picker in [SlewTargetWindow.xaml](src/OnStepX.Hub/Views/SlewTargetWindow.xaml).
- **Focuser** — Position bar, goto/jog, rate combos, backlash, TCF; auto-detects up to 6 firmware focusers via `:Fa#` / `:FA[n]#`.
- **Rotator** — Live angle dial (0° at top, CW), goto/jog, rate combos, backlash, derotator panel (auto-shown on AltAz / `:GX98#` reports `D`). Last-position card on cold boot offers a driver-side sync (no physical motion) since OnStepX firmware only persists rotator position via `park()`.
- **Advanced Settings** — Firmware parameters and diagnostics tabs ([AdvancedSettingsWindow.xaml](src/OnStepX.Hub/Views/AdvancedSettingsWindow.xaml)); NV reset lives here.
- **Notifications** — Windows toasts via `Notifications/NotificationService.cs` (Microsoft.Toolkit.Uwp.Notifications).
- **Theme** — Dark / light toggle (`Services/ThemeService.cs`), live without restart.

## Wire protocol

Tab-delimited envelope carrying raw LX200 strings — same pipe shared by every driver class (Telescope, Focuser, Rotator). No per-command opcodes; new mount commands ride the existing `CMD\t` / `BLIND\t` envelope without protocol changes. Out-of-band IPC verbs: `IPC:ISCONNECTED` (mount-link health probe), `IPC:SHOWHUB` (pop hub window), and `IPC:LIMIT\t<reason>` (driver → hub when a NINA-issued slew is firmware-rejected for a limit, so the hub flashes the same warning LED its in-app slew form uses). Full spec: [src/OnStepX.Shared/Hardware/Transport/PipeProtocol.md](src/OnStepX.Shared/Hardware/Transport/PipeProtocol.md).

Driver settings (`HKCU\Software\ASCOM\OnStepX`) are also a shared surface — the in-proc driver and the hub read/write the same keys (e.g. `RotatorSyncOffsetDeg`, `FocuserStepSize`) so client-issued state changes are visible in the hub UI without a protocol round-trip.

## Build

Requirements: Visual Studio 2022 or .NET SDK 6+, .NET Framework 4.8 Dev Pack, ASCOM Platform 6.6+ installed on the dev machine (driver's GAC references require the `ASCOM.DeviceInterfaces` and `ASCOM.Exceptions` v6 DLLs).

```
dotnet build OnStepX.sln -c Release
```

Artifacts:

- `src/OnStepX.Driver/bin/Release/ASCOM.OnStepX.dll` — bundles all three ASCOM classes.
- `src/OnStepX.Hub/bin/Release/OnStepX.Hub.exe` (+ `HelixToolkit.Wpf.dll`, `Microsoft.Toolkit.Uwp.Notifications.dll`)
- `src/OnStepX.Shared/bin/Release/OnStepX.Shared.dll`

## Register (dev, one-time, admin)

The installer writes CLSID keys directly; regasm is for developer round-trips. One regasm pass picks up every `[ComVisible]` class in the DLL.

```
installer\register.cmd
```

The ASCOM Chooser will list three entries from this bundle:

- **OnStepX Telescope Driver** (`ASCOM.OnStepX.Telescope`)
- **OnStepX Focuser Driver** (`ASCOM.OnStepX.Focuser`) — only useful if firmware has `AXIS4_DRIVER_MODEL` enabled.
- **OnStepX Rotator Driver** (`ASCOM.OnStepX.Rotator`) — only useful if firmware has `AXIS3_DRIVER_MODEL` enabled.

## Unregister

```
installer\unregister.cmd
```

## Build installer

Requires Inno Setup 6.

```
build-installer.cmd 0.5.0
```

Args are order-independent and accept `Debug` / `Release`; with `Debug` the ISCC step is skipped. Output: `installer\OnStepX-Setup-0.5.0.exe`. Writes the InprocServer32 keys for all three CLSIDs + `HKLM\SOFTWARE\OnStepX\Hub\InstallPath` consumed by the driver's auto-launcher.

## Running

1. Launch **OnStepX Hub** from the Start menu. Connect to your mount (Auto-Detect, or select COM/baud, or TCP host).
2. In NINA / SGP / etc., pick **OnStepX Telescope Driver** from the Chooser and hit Connect. If the firmware exposes a focuser or rotator, also pick the matching driver in NINA's Equipment tabs. The driver auto-launches the hub if it isn't already running.
3. The hub stays running after clients disconnect — useful as a standalone tool (catalogs, slew pad, manual LX200 console, 3D visualizer, focuser/rotator panels). Exit from the tray menu.
