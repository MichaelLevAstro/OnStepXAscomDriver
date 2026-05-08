# Polar Alignment Wedge Mode (Experimental)

When enabled, the OnStepX hub repurposes firmware **AXIS4** as the **Altitude** screw motor and **AXIS5** as the **Azimuth** screw motor of a motorized polar alignment wedge. The hub's Focuser and Rotator panels disappear, replaced by a **POLAR ALIGNMENT (Experimental)** section with manual jog controls. The hub also exposes a serial bridge that NINA's **Three Point Polar Alignment (TPPA)** plugin can drive via its built-in **OAPA System** profile — no custom NINA plugin needed.

## Firmware prerequisites

In OnStepX `Config.h`, enable **both** axes:

```c
#define AXIS4_DRIVER_MODEL  TMC2209  // or whatever driver you use
#define AXIS5_DRIVER_MODEL  TMC2209
```

Reflash. The hub auto-detects both axes via `:Fa#` / `:FA[1..6]#`. They appear on the wire as **focuser 1** + **focuser 2** (OnStepX's `:FA[n]#` numbers focusers, not physical axes — focuser 1 = AXIS4, focuser 2 = AXIS5).

## Enabling the mode in the hub

1. Expand the **ADVANCED** card in the left column.
2. Tick **"Enable Automatic Polar Alignment (Experimental)"**.

The toggle is **live** — no disconnect/reconnect needed. Within ~250 ms:

- The **POLAR ALIGNMENT (Experimental)** section appears in the right column.
- The Focuser and Rotator sections collapse out.
- The TPPA bridge auto-binds to the Hub-managed com0com pair.
- The ASCOM Focuser + Rotator drivers refuse Connect so third-party apps cannot accidentally drive the wedge.

Untick to revert — focuser/rotator are re-probed and their sections come back.

## Manual jog panel

Plus-shape pad with five buttons + one shared STOP:

```
       ↑
  ←  STOP  →
       ↓
```

- **Up / Down** = Alt screw (focuser 1, AXIS4)
- **Left / Right** = Az screw (focuser 2, AXIS5)
- **STOP** halts both axes (`:FQ#` on each)

Each click moves the per-axis **Step** amount at the **Slew speed** dropdown's selected rate. Per-axis **Goto** field + button issues an absolute move.

Speed mapping (OnStepX goto-rate band, applies to all moves):

| Speed     | Preset | Rate |
| --------- | ------ | ---- |
| Slow      | `:F5#` | 0.5× |
| Fast      | `:F7#` | 1×   |
| Very Fast | `:F9#` | 2×   |

### Driver currents (Advanced popup)

Click **Advanced…** in the section header to open the popup. Per-axis:

- **Run (mA)** — motor current during slews. Higher = more torque, more heat.
- **Hold (%)** — standstill current as % of run. Higher = better against drift / vibration, more standby heat.

Apply pushes `:SXAn,IRUN=<mA>#` / `:SXAn,IHOLD=<%>#` to firmware (n = 4 for Alt, 5 for Az). Persisted to registry and reapplied on reconnect.

NINA's OAPA plugin can also write these values via its serial command set (`XC<mA>`, `XH<%>`, `YC<mA>`, `YH<%>`). The bridge forwards those to the same firmware commands.

## NINA TPPA bridge (virtual COM port pair)

NINA's TPPA OAPA profile auto-discovers polar alignment hardware by scanning serial ports for a GRBL-style status reply. The hub presents itself on one half of a **virtual COM port pair**; NINA opens the other half.

### Automatic setup

The OnStepX installer ships **com0com** and creates one virtual pair at install time (first free `COM<N>` / `COM<N+1>` ≥ COM10, ComDB-aware to skip stale claims). The hub auto-binds its bridge to that pair the first time you tick **Enable Automatic Polar Alignment**. Nothing else to configure.

Verify after install:

1. Tick **ADVANCED → Enable Automatic Polar Alignment (Experimental)**.
2. The hub console (toggle from the bottom of the window) should show `PABRIDGE  started on COM<N>`.
3. In NINA → TPPA plugin → set **Polar Alignment System = OAPA System**, scan ports → it will find the partner end (`COM<N+1>`).

### Virtual port pairs

Pair creation/deletion happens once during the installer's elevated phase. Hub reads the resulting pair from `HKLM\SOFTWARE\OnStepX\Hub\Com0comManagedPairs` (`REG_SZ`, `<pairNum>|<PortA>|<PortB>` semicolon-separated) and binds without elevation.
Hub's created port pair is removed when hub is uninstalled.

If `TppaBridgePort` ever points at a port that is not in the managed list (stale registry value, user mistakenly entered the NINA-side port, etc.), the hub auto-resets it to the first managed pair's A side at startup.

### Managing extra pairs

If you need a second pair (multiple hub instances, you accidentally deleted the bundled one, etc.), open the **com0com Setup Command Prompt** from the Start menu (admin) and run:

```
setupc install PortName=COM<N> PortName=COM<N+1>
```

Hub continues to bind to whichever pair is in `Com0comManagedPairs`. To repoint, edit that registry value (or uninstall + reinstall to regenerate).

### Manual setup (only if the bundled driver did not install)

If your machine rejects the bundled signed kernel driver (Secure Boot configuration, conflicting older com0com install, etc.), fall back to a manual com0com install:

1. Install **com0com** (free, GPL): <https://com0com.sourceforge.net/>.
2. Open **Setup Command Prompt for com0com** as Administrator → `install PortName=COM10 PortName=COM11`.
3. Manually write `HKLM\SOFTWARE\OnStepX\Hub\Com0comManagedPairs` (`REG_SZ`) with value `0|COM10|COM11`.
4. Restart the hub. It will auto-default `TppaBridgePort = COM10`.

### NINA side

1. NINA → **TPPA plugin → Options**.
2. Set **Polar Alignment System** = `OAPA System`.
3. The plugin scans 115200 8N1 ports and binds to whichever answers the OAPA `?` query. With the bundled pair, this is the partner end of the pair the hub bound to.
4. Configure **XGearRatio** and **YGearRatio** in the plugin settings: number of OnStepX motor steps per arcminute of mount-axis rotation. Calibrate empirically:
   - Move axis 4 (Alt) by a known number of steps via hub Goto.
   - Measure mount altitude change (e.g. plate-solve before/after).
   - `gearRatio = steps / arcminutes_observed`.

### Running an alignment

In NINA, drop a **Polar Alignment** sequence item. With OAPA connected and mount talking ASCOM, TPPA will:

1. Capture three plate-solved positions at different RA points.
2. Compute Az/Alt error from the polar-axis vector.
3. Loop: capture → solve → `MoveCloser` → wedge motors run → settle → recapture, until error ≤ tolerance.

Each iteration moves only the larger error axis by 75 % of measured error.

## Section state persistence

The hub remembers which sections you had expanded/collapsed across launches (per-section, registry-backed under `HKCU\Software\ASCOM\OnStepX\Section.<name>.Expanded`). First-run defaults: SITE, DATE / TIME, LIMITS, ADVANCED, CURRENT POSITION, VISUALIZER all start collapsed; everything else starts expanded.

## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| POLAR ALIGNMENT section doesn't appear after toggling | Mount must be connected. Toggle is live but PA mode resolution requires firmware focuser probe. |
| ASCOM Focuser/Rotator driver refuses Connect | Expected when PA mode is on. Untick the toggle to use focuser/rotator. |
| NINA OAPA doesn't find a port | Hub console should show `PABRIDGE started on COMxx`. Verify the com0com pair exists (`mode` in cmd, or Device Manager). |
| `:FA1#` / `:FA2#` rejected in console | Firmware doesn't have AXIS4 + AXIS5 both enabled. Check `Config.h` and reflash. |
| Wedge moves wrong direction | Set `ReverseAzimuth` / `ReverseAltitude` in NINA TPPA settings, or flip `AXIS4_REVERSE` / `AXIS5_REVERSE` in firmware. |
| First TPPA iteration overshoots | Gear ratio too high. Halve `XGearRatio` / `YGearRatio` and retry. |
| Motor stalls or skips steps | Bump **Run (mA)** in Advanced popup. Default 500 mA — try 800-1200 mA depending on stepper. |
| Wedge drifts when idle | Bump **Hold (%)** in Advanced popup. Default 50 % — try 70-90 % for high-load wedges. |
| Wedge hits hard stop | Reduce per-axis Step in the panel. TPPA's auto-correction caps at 75 % per iteration so it won't run away. |
| NINA throws "Motor appears stuck" | Hub console should show motor moving (`:Fg#` value changing). If position never changes, firmware isn't accepting commands — try lower goto-rate (slower speed) or check wiring. |
| Hub binds bridge to wrong port | Hub auto-routes `TppaBridgePort` to the first entry in `Com0comManagedPairs`. Edit that registry value to repoint. |
| Position resets to old value after power-cycle | Known firmware quirk in upstream OnStepX: focuser axis position only persists to NV after `FOCUSER_WRITE_DELAY` seconds idle. Wait long enough between last move and power-off. |
