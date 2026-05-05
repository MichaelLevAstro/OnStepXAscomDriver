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

## NINA TPPA bridge (com0com setup)

NINA's TPPA OAPA profile auto-discovers polar alignment hardware by scanning serial ports for a GRBL-style status reply. To present the hub on a port that NINA can find, you need a **virtual COM port pair**.

### One-time setup

1. Install **com0com** (free, GPL): <https://com0com.sourceforge.net/>.
2. Open **Setup Command Prompt for com0com** as Administrator.
3. `install PortName=COM10 PortName=COM11` (or run the GUI Setup, add pair).
4. Verify in Device Manager → Ports (COM & LPT). May appear under **com0com** category if "use Ports class" wasn't ticked — works either way.

### Hub side

1. Hub → **ADVANCED card → TPPA port**: enter `COM10` (the first half of the com0com pair).
2. Click outside the field to commit. Hub opens `COM10` and starts speaking OAPA on it. Confirm in console: `PABRIDGE  started on COM10`.

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
