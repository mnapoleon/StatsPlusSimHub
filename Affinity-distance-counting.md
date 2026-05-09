# Affinity Distance And Lap Counting

This note explains how `Affinity` currently counts distance and laps, and where the behavior differs by game.

## Scope

Affinity stores cumulative totals by:

- `game`
- `car`
- `track`

Internally, the active bucket key is:

- `gameName | carModel | trackNameWithConfig`

That means the live context total at the top of the plugin is for the current `game / car / track` combination only.

## Lap Counting

Affinity does not infer laps from distance.

It uses the sim-provided `CompletedLaps` value and stores only positive changes:

- `lapDelta = currentCompletedLaps - lastObservedCompletedLaps`
- if `lapDelta > 0`, Affinity adds that delta to the current bucket

Implications:

- If a sim counts an out lap as a completed lap, Affinity will also count it.
- If a sim delays or advances its lap increment slightly relative to track position, Affinity follows the sim’s lap counter.
- Lap totals are cumulative within each `game / car / track` bucket.

## Distance Counting

Affinity tracks a per-session distance baseline and then adds only positive session-distance deltas into the current bucket.

High-level flow:

1. Choose a session distance source for the session.
2. Establish a session origin.
3. On each update, compute `sessionMeters = absoluteSessionMeters - origin`.
4. Add only positive deltas to the bucket.

## Distance Sources

Affinity can use one of these sources:

- `Derived`
- `SessionOdoMeters`
- `SessionOdoKilometers`

### Derived

Derived distance is computed from lap count and track position:

- `CompletedLaps * TrackLength + TrackPositionWithinLap`

If `TrackPositionMeters` is already larger than one lap length, it is treated as an already-cumulative value.

### SessionOdoMeters

Uses the raw `SessionOdo` value directly as meters.

### SessionOdoKilometers

Uses `SessionOdo * 1000`.

## Game-Specific Rules

### Assetto Corsa

Games matched:

- `assettocorsa`

Distance source:

- always `Derived`

Why:

- The SimHub-exposed `SessionOdo` value was not reliable as a clean session odometer.

Session origin:

- forced to `0` when using the derived source

Why:

- This preserves real pit-lane and out-lap driving instead of subtracting it away as a startup offset.

Extra guards:

- ignores obvious startup telemetry snaps when the car is effectively stationary

### Assetto Corsa EVO

Games matched:

- `assettocorsaevo`

Distance source:

- always `Derived`

Session origin:

- forced to `0` when using the derived source

Behavior is intended to mirror classic Assetto Corsa.

### RaceRoom / R3E / RRRE

Games matched:

- `raceroomracingexperience`
- `r3e`
- `rrre`

Distance source:

- always `Derived`

Why:

- The SimHub `SessionOdo` field was observed to scale in a way that caused wildly inflated totals.

Extra guards:

- derived lap-wrap guard at the start/finish line

Why:

- In RaceRoom, `TrackPosition` can wrap to near zero one frame before `CompletedLaps` increments.
- Without a guard, that can look like a session reset followed by an extra full-lap jump, which double-counts one lap.

### Other Games

For other games, Affinity currently auto-selects a source at session start:

- compare `Derived`
- compare raw `SessionOdo` as meters
- compare `SessionOdo * 1000`
- lock the closest plausible source for the rest of the session

This is a heuristic and may need game-specific overrides if a title exposes ambiguous telemetry.

## Session Start Behavior

At session start, Affinity records:

- current session id
- active bucket key
- chosen distance source
- session origin
- current completed lap count

For derived-source AC/ACE/RRRE sessions, the origin is intentionally `0`.

For other games, the origin is the chosen absolute session distance at the time the session begins.

## Reset And Wrap Handling

Affinity has a few protections for bad telemetry transitions:

- startup position snap guard
  - ignores large initial jumps while stationary
- session distance reset handling
  - if session distance drops materially, Affinity updates its local baseline instead of adding negative distance
- derived lap-wrap guard
  - for derived-source sims, if track position wraps by about one lap before the lap counter increments, Affinity waits for lap-counter sync instead of treating that wrap as a real reset

## What The UI Shows

### Top Status Area

Top values are current live context values:

- current total distance for the active `game / car / track`
- current session distance
- current total laps for the active `game / car / track`
- current session laps

### Game Tabs

Each game tab is cumulative for that game.

### Track Table

The track table is grouped by track only, across all cars in that game.

That means:

- running the same track in a different car increases the same track row

### Car Table

The car table is grouped by car only, across all tracks in that game.

## Practical Implications

- Affinity is cumulative, not per-run.
- If you want to validate one clean test, clear the distance data first.
- If a sim’s other overlays show `CompletedLaps = 3`, Affinity will also store `3` laps if the lap counter advanced that way.
- Raw distance and lap semantics can still differ by sim, especially around pit starts and out laps.

## Debug Logging

When enabled in the current build, targeted debug logging writes to:

- `C:\Program Files (x86)\SimHub\PluginsData\Common\Affinity.distance.debug.log`

The debug log is especially useful for:

- `RaceRoom`
- `AssettoCorsaEVO`

It records:

- selected distance source
- origin
- raw `SessionOdo`
- `TrackPositionMeters`
- `TrackPositionPercent`
- `TrackLength`
- derived session meters
- session deltas
- lap deltas
- reset and wrap events
