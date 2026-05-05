# Polar Alignment Wedge Mode

When enabled, the OnStepX hub repurposes firmware **AXIS4** as the **Altitude** screw motor and **AXIS5** as the **Azimuth** screw motor of a motorized polar alignment wedge. The hub's Focuser and Rotator panels disappear, replaced by a Polar Alignment panel with manual jog controls. The hub also exposes a serial bridge that NINA's **Three Point Polar Alignment (TPPA)** plugin can drive via its built-in UPAS profile — no custom NINA plugin needed.

## Firmware prerequisites

In OnStepX `Config.h`, enable **both** axes:

```c
#define AXIS4_DRIVER_MODEL  TMC2209  // (or whatever driver you use)
#define AXIS5_DRIVER_MODEL  TMC2209
```

Reflash. The hub auto-detects both axes via `:Fa#` / `:FA[1..6]#`.

## Enabling the mode in the hub

1. Open the **Hub → Advanced Settings**.
2. Tick **"Use AXIS4 + AXIS5 as Polar Alignment Wedge"**.
3. Click **Apply**, then disconnect and reconnect to the mount.

You should now see the **POLAR ALIGNMENT** section in the right column. The Focuser and Rotator sections are gone (and the ASCOM Focuser + Rotator drivers will refuse Connect — third-party apps cannot accidentally drive the wedge).

## Manual jog panel

Two rows of seven buttons each (one row per axis):

```
Alt:  [«« VF] [«« F] [« S]  [STOP]  [S »] [F »»] [VF »»]
Az:   [«« VF] [«« F] [« S]  [STOP]  [S »] [F »»] [VF »»]
```

Each click moves **StepSize** motor steps at the chosen rate (Slow / Fast / VeryFast). Step size is configurable per axis in the panel; this caps how far one click can move so a misclick doesn't drive the wedge into a hard stop. STOP halts both axes.

Speed mapping (OnStepX goto-rate band):

| Button | Preset | Rate |
| ------ | ------ | ---- |
| S      | `:F5#` | 0.5× |
| F      | `:F7#` | 1×   |
| VF     | `:F9#` | 2×   |

## NINA TPPA bridge (com0com setup)

NINA's TPPA UPAS profile auto-discovers polar alignment hardware by scanning serial ports for a GRBL-style status reply. To present the hub on a port that NINA can find, you need a **virtual COM port pair**.

### One-time setup

1. Install **com0com** (free, GPL): <https://com0com.sourceforge.net/>.
2. Run the **Setup** GUI as Administrator.
3. Add a new pair (e.g. `COM10` ↔ `COM11`). Tick "use Ports class" on both halves so Windows lists them in the COM port chooser.

### Hub side

1. **Hub → Advanced Settings → NINA TPPA bridge port**: enter `COM10` (the first half of the com0com pair).
2. Click **Apply**. The hub opens `COM10` and starts speaking GRBL on it. Confirm in the hub console log: `PABRIDGE  started on COM10`.

### NINA side

1. In NINA, open the **TPPA plugin → Options**.
2. Set **Polar Alignment System** = `UPAS` (Avalon Universal Polar Alignment).
3. The plugin auto-scans serial ports at 115200 8N1 and binds to whichever port answers the GRBL `?` query. With com0com paired, this will be `COM11`.
4. Configure **XGearRatio** and **YGearRatio** in the plugin settings: this is the number of OnStepX motor steps per arcminute of mount-axis rotation. Calibrate empirically:
   - Move axis 4 (Alt) by a known number of steps.
   - Measure the change in mount altitude (e.g. via plate-solve before/after).
   - `gearRatio = steps / arcminutes_observed`.

### Running an alignment

In NINA, drop a **Polar Alignment** sequence item. With the TPPA system connected and your mount talking ASCOM, TPPA will:

1. Capture three plate-solved positions at different RA points.
2. Compute the current Az/Alt error from the polar axis vector.
3. Loop: capture → solve → call `MoveCloser` → wedge motors run → settle → recapture, until error ≤ tolerance.

The continuous-correction loop drives the bridge's `MoveRelative` calls. Each iteration moves only the larger error axis by 75% of the measured error.

## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| POLAR ALIGNMENT section doesn't appear after toggling | Reconnect to the mount. Mode is resolved at connect time. |
| ASCOM Focuser/Rotator driver refuses Connect | Expected when PA mode is on. Disable PA mode if you actually want focuser/rotator. |
| NINA TPPA UPAS doesn't find a port | Is the bridge running? Hub log should say `PABRIDGE started on COM10`. Is the com0com pair installed and visible in Device Manager → Ports (COM & LPT)? |
| Wedge moves the wrong direction | Set `ReverseAzimuth` / `ReverseAltitude` in NINA TPPA settings, or flip `AXIS4_REVERSE` / `AXIS5_REVERSE` in firmware. |
| First TPPA iteration overshoots wildly | Gear ratio is too high. Halve `XGearRatio` / `YGearRatio` and retry. |
| Wedge hits hard stop | Reduce per-axis StepSize in the manual panel. TPPA's auto-correction is already capped at 75% of error per iteration so it won't run away. |
