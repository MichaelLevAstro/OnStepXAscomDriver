# Polar Alignment Wedge Mode

When enabled, the OnStepX hub repurposes firmware **AXIS4** as the **Altitude** screw motor and **AXIS5** as the **Azimuth** screw motor of a motorized polar alignment wedge. The hub's Focuser and Rotator panels disappear, replaced by a Polar Alignment panel with manual jog controls. The hub also exposes a serial bridge that NINA's **Three Point Polar Alignment (TPPA)** plugin can drive via its built-in **OAPA** profile — no custom NINA plugin needed.

## Firmware prerequisites

In OnStepX `Config.h`, enable **both** axes:

```c
#define AXIS4_DRIVER_MODEL  TMC2209  // (or whatever driver you use)
#define AXIS5_DRIVER_MODEL  TMC2209
```

Reflash. The hub auto-detects both axes via `:Fa#` / `:FA[1..6]#`. They appear on the wire as **focuser 1** + **focuser 2** (OnStepX's `:FA[n]#` numbers focusers, not physical axes).

## Enabling the mode in the hub

1. Open the **Hub → ADVANCED card** in the left column (or the modal Advanced Settings).
2. Tick **"Use AXIS4 + AXIS5 as Polar Alignment Wedge"**.
3. Disconnect and reconnect to the mount.

You should now see the **POLAR ALIGNMENT** section in the right column. The Focuser and Rotator sections are gone, and the ASCOM Focuser + Rotator drivers refuse Connect so third-party apps cannot accidentally drive the wedge.

## Manual jog panel

Plus-shape pad with five buttons + one shared STOP:

```
       ↑
  ←  STOP  →
       ↓
```

- **Up / Down** = Alt screw (focuser 1)
- **Left / Right** = Az screw (focuser 2)
- **STOP** halts both axes

Each click moves the per-axis **Step** amount at the **Slew speed** dropdown's selected rate. Per-axis **Goto** field + button issues an absolute move.

Speed mapping (OnStepX goto-rate band, applies to all moves):

| Speed     | Preset | Rate |
| --------- | ------ | ---- |
| Slow      | `:F5#` | 0.5× |
| Fast      | `:F7#` | 1×   |
| Very Fast | `:F9#` | 2×   |

### Driver currents (Advanced popup)

Click **Advanced…** in the section header to open the TMC tuning popup. Per-axis:

- **Run (mA)** — motor current during slews. Higher = more torque, more heat.
- **Hold (%)** — standstill current as % of run. Higher = better against drift / vibration, more standby heat.

Apply pushes `:SXAn,IRUN=<mA>#` / `:SXAn,IHOLD=<%>#` to firmware (n=4 for Alt, n=5 for Az). Persisted across reconnects.

NINA's OAPA plugin can also write these values via its serial command set (`XC<mA>`, `XH<%>`, `YC<mA>`, `YH<%>`). The bridge forwards those to the same firmware commands.

## NINA TPPA bridge (virtual COM port pair)

NINA's TPPA OAPA profile auto-discovers polar alignment hardware by scanning serial ports for a GRBL-style status reply. The hub presents itself on one half of a **virtual COM port pair**; NINA opens the other half.

### Automatic setup

The OnStepX installer ships **com0com** and creates one virtual pair on install (default: first free `COM<N>` / `COM<N+1>` ≥ COM10, ComDB-aware to skip stale claims). The hub auto-binds its bridge to that pair the first time you tick **Enable Automatic Polar Alignment** in the ADVANCED card. Nothing else to configure.

Verify after install:

1. Tick **ADVANCED → Enable Automatic Polar Alignment**.
2. Hub console should show `PABRIDGE  started on COM<N>`.
3. In NINA → TPPA plugin → set **Polar Alignment System = OAPA System**, scan ports → it will find the partner end (`COM<N+1>`).

### Managing extra pairs (rare)

Hub does **not** create or delete virtual ports at runtime — that would require admin per click. The bundled installer is the only path that touches com0com, so the hub itself never asks for elevation.

If you need a second pair (e.g. multiple hub instances, or you accidentally deleted the first one), open the **com0com Setup Command Prompt** from the Start menu and run:

```
setupc install PortName=COM<N> PortName=COM<N+1>
```

Then point NINA at the new pair manually. The hub will still bind to whichever pair is recorded under `HKLM\SOFTWARE\OnStepX\Hub\Com0comManagedPairs` (the installer-created pair).

### Manual setup (only if the bundled driver did not install)

If your machine rejects the bundled signed kernel driver (Secure Boot configuration, conflicting older com0com install, etc.), fall back to a manual com0com install:

1. Install **com0com** (free, GPL): <https://com0com.sourceforge.net/>.
2. Open **Setup Command Prompt for com0com** as Administrator → `install PortName=COM10 PortName=COM11`.
3. Manually write `HKLM\SOFTWARE\OnStepX\Hub\Com0comManagedPairs` (REG_SZ) with value `0|COM10|COM11` so the hub picks it up.

### NINA side

1. NINA → **TPPA plugin → Options**.
2. Set **Polar Alignment System** = `OAPA System`.
3. Plugin scans 115200 8N1 ports and binds to whichever answers the OAPA `?` query. With com0com paired, this is `COM11`.
4. Configure **XGearRatio** and **YGearRatio** in the plugin settings: number of OnStepX motor steps per arcminute of mount-axis rotation. Calibrate empirically:
   - Move axis 4 (Alt) by a known number of steps via hub Goto.
   - Measure mount altitude change (e.g. plate-solve before/after).
   - `gearRatio = steps / arcminutes_observed`.

### Running an alignment

In NINA, drop a **Polar Alignment** sequence item. With OAPA connected and mount talking ASCOM, TPPA will:

1. Capture three plate-solved positions at different RA points.
2. Compute Az/Alt error from the polar-axis vector.
3. Loop: capture → solve → `MoveCloser` → wedge motors run → settle → recapture, until error ≤ tolerance.

Each iteration moves only the larger error axis by 75% of measured error.

## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| POLAR ALIGNMENT section doesn't appear after toggling | Reconnect to the mount. Mode resolves at connect time. |
| ASCOM Focuser/Rotator driver refuses Connect | Expected when PA mode is on. Disable PA mode to use focuser/rotator. |
| NINA OAPA doesn't find a port | Is the bridge running? Hub console should show `PABRIDGE started on COMxx`. Is the com0com pair installed? |
| `:FA1#` / `:FA2#` rejected in console | Firmware doesn't have AXIS4 + AXIS5 both enabled. Check `Config.h` and reflash. |
| Wedge moves wrong direction | Set `ReverseAzimuth` / `ReverseAltitude` in NINA TPPA settings, or flip `AXIS4_REVERSE` / `AXIS5_REVERSE` in firmware. |
| First TPPA iteration overshoots | Gear ratio too high. Halve `XGearRatio` / `YGearRatio` and retry. |
| Motor stalls or skips steps | Bump **Run (mA)** in Advanced popup. Default 500 mA — try 800-1200 mA depending on stepper. |
| Wedge drifts when idle | Bump **Hold (%)** in Advanced popup. Default 50% — try 70-90% for high-load wedges. |
| Wedge hits hard stop | Reduce per-axis Step in the panel. TPPA's auto-correction caps at 75% per iteration so it won't run away. |
| NINA throws "Motor appears stuck" | Hub log should show motor moving (`:Fg#` value changing). If position never changes, firmware isn't accepting commands — try lower goto-rate (slower speed) or check wiring. |
