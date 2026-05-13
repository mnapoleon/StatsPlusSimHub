# Affinity Distance Debug Summary

Date: 2026-05-07

## What We Fixed

- `RRRE` had a real distance-source bug.
- We changed the logic so it no longer blindly treats `SessionOdo` as kilometers.
- We also stopped double-counting when `TrackPositionMeters` is already cumulative.

- Classic `Assetto Corsa` was being distorted by a bad `SessionOdo` source and startup offsets.
- We changed AC to ignore `SessionOdo`, use derived lap-position distance, and handle startup telemetry more safely.
- We later moved AC and AC Evo onto the same stateful forward track-position model used by AMS2/iRacing/rFactor2, because line-start sessions could still undercount when the first wrap happened before the lap counter incremented.

## What We Confirmed

- Affinity stores data separately by `game / car / track`.
- The live file is:
  - `C:\Program Files (x86)\SimHub\PluginsData\Common\Affinity.distance.json`
- There is no car-bucket collision in the JSON.
- The track table in the UI is aggregated by track only, across cars.
- Because of that, an older track row can increase after a run in a different car on the same track.

## Current Status By Sim

### RaceRoom / RRRE

- Earlier bug was real and significant.
- The current logic is much healthier than before.

### Assetto Corsa

- Current behavior looks correct.
- Exact 2-lap Red Bull Ring National test came back at about `4.69 km` on a `2336 m` track.
- That is essentially correct.
- A later Canadian Tire Motorsports Park test on a `3913.99 m` telemetry track finished at `7877.23 m` with `CompletedLaps = 2`, which is also correct for 2 laps plus a small amount beyond the line.

### Assetto Corsa EVO

- Earlier Evo runs were wrong and showed the same general class of issue as AC around lap/position semantics.
- AC Evo now follows the same stateful forward track-position model as classic AC.

### Automobilista 2

- Observed a severe inflation case on Red Bull National/Short.
- The stored bucket initially showed about `4,484,685 m`, which surfaced in the UI as roughly `4484.69 km`.
- That confirmed the generic distance-source heuristic was trusting a bad `SessionOdo` interpretation instead of a true session-distance value.
- AMS2 now ignores `SessionOdo`, writes targeted debug logs, and uses a monotonic derived-distance model based on forward track-position movement across lap wraps.
- On a clean retest at Red Bull National/Short, the plugin recorded about `7.22 km` for an out lap from pits plus `2` full laps on a `2328.24 m` track, and the live session distance matched the stored JSON total.

### iRacing

- iRacing also needed to stop depending on lap-count-derived cumulative distance.
- A Red Bull Ring National test showed the live derived session distance reaching only about `4.86 km`, while the stored bucket inflated to `11.43 km` because telemetry briefly dropped to `0` laps / `0` position and then snapped back.
- iRacing now ignores `SessionOdo`, uses the same monotonic forward track-position model as AMS2, and suppresses transient zeroed telemetry frames so distance and lap totals are not counted twice.

### rFactor 2

- A Lime Rock Park test showed rFactor 2 starting near the timing line and then oscillating `TrackPosition` across the line at low speed while leaving the garage and pit area.
- The generic derived path was noisy there, and the sim also reported an extra completed lap when the car ended up essentially stopped on the line.
- rFactor 2 now ignores `SessionOdo`, uses the same stateful forward track-position model as AMS2 and iRacing, suppresses low-speed line-wrap noise near pit exit, and ignores near-stationary lap increments at the line.

## Important Telemetry Finding

In classic `Assetto Corsa`, the SimHub overlay field labeled `SessionOdo` does not behave like a trustworthy per-session odometer.

Observed values:

- startup before moving: `0.3`
- after 2 laps: `2936`

This does not line up cleanly with meters, kilometers, or miles, so we stopped trusting it for AC.

## Evidence From Live JSON

From `Affinity.distance.json`:

- `AssettoCorsa / Mercedes SLS AMG / ks_red_bull_ring-layout_national`
  - `4693.34 m`
  - `CompletedLaps: 2`

- `AssettoCorsaEVO / Abarth 695 Biposto / Red Bull Ring-National`
  - `8702.60 m`
  - `CompletedLaps: 1`

- `AssettoCorsaEVO / Alfa Romeo Junior Veloce / Red Bull Ring-National`
  - `8572.72 m`
  - `CompletedLaps: 1`

## Best Next Step

If we come back to this, the best next move is targeted telemetry follow-up for `AssettoCorsaEVO`:

- `CompletedLaps`
- `TrackPositionMeters`
- `TrackPositionPercent`
- `TrackLength`
- `ReportedTrackLength`
- `SessionOdo`
- chosen distance source
- computed absolute/session meters

That should let us fix Evo based on real telemetry behavior instead of more guessing.
