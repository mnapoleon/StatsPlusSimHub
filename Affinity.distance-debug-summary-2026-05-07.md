# Affinity Distance Debug Summary

Date: 2026-05-07

## What We Fixed

- `RRRE` had a real distance-source bug.
- We changed the logic so it no longer blindly treats `SessionOdo` as kilometers.
- We also stopped double-counting when `TrackPositionMeters` is already cumulative.

- Classic `Assetto Corsa` was being distorted by a bad `SessionOdo` source and startup offsets.
- We changed AC to ignore `SessionOdo`, use derived lap-position distance, and handle startup telemetry more safely.

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

### Assetto Corsa EVO

- Still wrong.
- Two separate Evo cars each recorded about `8.6-8.7 km` on Red Bull Ring National.
- Both stored only `CompletedLaps: 1`.
- That strongly suggests AC Evo telemetry semantics differ from classic AC, and the current derived-distance logic is still interpreting Evo fields incorrectly.

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

If we come back to this, the best next move is targeted temporary logging for `AssettoCorsaEVO` only:

- `CompletedLaps`
- `TrackPositionMeters`
- `TrackPositionPercent`
- `TrackLength`
- `ReportedTrackLength`
- `SessionOdo`
- chosen distance source
- computed absolute/session meters

That should let us fix Evo based on real telemetry behavior instead of more guessing.
