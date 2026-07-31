# StatsPlus New Lap Hook Follow-Up

## Context

This branch is intended for the Affinity-style StatsPlus UI changes. During testing, Assetto Corsa exposed intermittent missed/misattributed lap captures in the current StatsPlus polling flow.

## Finding

SimHub's built-in Statistics plugin records laps through a dedicated new-lap path rather than only polling in `DataUpdate`.

The built-in plugin exposes:

```csharp
TrackNewLap(int completedLapNumber, bool testLap, ref GameData data, Dictionary<int, SectorHistoryEntry> sectorTimes)
```

That path receives the completed lap number, current `GameData`, and SimHub sector history for the completed lap. The built-in plugin then fills a `DataRecord` using `LapId`, `SessionId`, `SessionStartDate`, `LastLapTime`, sector data, and accumulated telemetry before writing to SimHub's per-game `DataStore.db`.

StatsPlus currently records from `DataUpdate` by detecting `CompletedLaps` changes, queueing a pending lap, and reading `NewData.LastLapTime` once it appears fresh. This is more timing-sensitive, especially for Assetto Corsa where `LastLapTime` can look unchanged at the lap boundary even when it already represents the newly completed lap.

## Follow-Up

After the UI branch is settled, investigate moving StatsPlus lap capture toward SimHub's new-lap hook and keeping the existing `DataUpdate` polling path only as a guarded fallback during transition.

Key goals:

- Avoid double-recording when both paths are active.
- Use SimHub-provided lap identity (`LapId`, `SessionId`) when available.
- Prefer SimHub's `sectorTimes` dictionary for completed-lap sectors.
- Preserve StatsPlus-specific logging under `PluginsData/StatsPlus`.
- Keep tests for Assetto Corsa stable same-time laps, first-lap capture, and game-stop flush behavior.
