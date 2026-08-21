# StatsPlus SimHub Plugin

StatsPlus is a SimHub plugin for recording and reviewing personal lap history. It tracks laps by game, car, and track variation, exposes personal-best properties back to SimHub, and provides a settings/history UI for reviewing stored laps.

## Current Storage Model

Runtime lap history is stored in LiteDB:

```text
C:\Program Files (x86)\SimHub\PluginsData\StatsPlus\StatsPlus.laps.ldb
```

Plugin settings still use JSON:

```text
C:\Program Files (x86)\SimHub\PluginsData\StatsPlus\StatsPlus.settings.json
```

`StatsPlus.laps.json` is no longer used for lap history. The plugin does not read it, write it, import it, or fall back to it.

SQLite is no longer part of the runtime plugin. It exists only in `StatsPlus.Migration` so existing SQLite lap data can be moved once into LiteDB.

## LiteDB Shape

The LiteDB store uses two collections:

- `trackHistories`
- `laps`

`trackHistories` contains one document per game/car/track variation. It stores normalized identity fields for lookups and original/display fields for UI text.

Important fields:

- `GameName`
- `NormalizedGameName`
- `CarModel`
- `NormalizedModelName`
- `DisplayModelName`
- `RawTrackName`
- `TrackNameWithConfig`
- `NormalizedTrackNameWithConfig`
- `DisplayTrackNameWithConfig`
- `CreatedUtc`
- `LastUpdatedUtc`

StatsPlus resolves supported games through lightweight game profiles. Profiles own recording-toggle lookup, supported-game/debug metadata, track display mapping, circuit/layout display, sector-layout inference, and game-specific lap-boundary capabilities. `StatsPlusPlugin` owns game-agnostic telemetry orchestration and resolves a profile instead of classifying games directly.

Stored-history UI rows resolve the raw `TrackNameWithConfig` into display-oriented fields before binding:

- `TrackNameWithConfigDisplay` keeps the full resolved track display text.
- `CircuitNameDisplay` and `CircuitLayoutDisplay` mirror Affinity's track display columns.
- `CarModelDisplay` keeps a display-friendly car label when available.

Raw `GameName`, `CarModel`, and `TrackNameWithConfig` remain the lookup identity for lap rows and personal-best properties. Display fields are intentionally fuzzy search targets. An Affinity-style track search should match `TrackNameWithConfigDisplay`, `CircuitNameDisplay`, and `CircuitLayoutDisplay` together. Future exact filters should query raw `GameName`, `CarModel`, `RawTrackName`, and `TrackNameWithConfig`; if exact layout filtering is needed, add a separate transient raw layout key then instead of treating `CircuitLayoutDisplay` as a durable identity.

`laps` contains one document per recorded lap.

Important fields:

- `TrackHistoryId`
- `LapNumber`
- `LapTimeSeconds`
- `Sector1Seconds`
- `Sector2Seconds`
- `Sector3Seconds`
- `IsValid`
- `TimestampUtc`

`CreatedUtc` is set when a track-history document is first created. Later sessions do not rewrite it. `LastUpdatedUtc` advances only when a newer lap timestamp is recorded. `laps.TimestampUtc` is the source of truth for when the lap happened.

Laps are not embedded inside `trackHistories`; they are separate documents so validity toggles and history growth do not rewrite a large parent document.

## SimHub LiteDB Compatibility

SimHub v9.11.21 ships with `LiteDB.dll` assembly version `4.1.4.0`, and its built-in Statistics/Lap History plugin depends on that version.

StatsPlus must therefore target LiteDB `4.1.4` at runtime. Do not copy LiteDB `5.x` into the SimHub root folder. Doing so breaks SimHub's built-in Statistics plugin with an error like:

```text
Could not load file or assembly 'LiteDB, Version=4.1.4.0'
at SimHub.Plugins.DataPlugins.PersistantTracker.HistoryModel.RefreshSessions()
```

The `StatsPlus.csproj` deploy target now excludes these files from copy-to-SimHub:

- `LiteDB.dll`
- `System.Buffers.dll`

That keeps SimHub's shared dependencies intact while still copying the updated `StatsPlus.dll`.

Because LiteDB `4.1.4` is old, NuGet reports a known vulnerability warning (`NU1904`). In this plugin-hosted setup, matching SimHub's in-process assembly is currently the safer practical choice. Upgrading runtime LiteDB would require isolating the assembly from SimHub's root load context or changing SimHub's own dependency set, both of which are riskier.

## UTC Timestamp Lesson

LiteDB 4 can round-trip `DateTime` through local time. To keep session dates stable, StatsPlus stores UTC timestamps as ticks in LiteDB while exposing `DateTime` properties in code.

The persisted field names remain:

- `CreatedUtc`
- `LastUpdatedUtc`
- `TimestampUtc`

Internally those fields are stored as UTC ticks, so migration and runtime reads preserve exact UTC values regardless of the machine timezone.

## Migration Utility

Use `StatsPlus.Migration` to convert an existing SQLite database into the LiteDB document structure:

```powershell
cd C:\Users\micha\dev\StatsPlusSimHub

.\StatsPlus.Migration\bin\Debug\net48\StatsPlus.Migration.exe `
  --source "C:\Program Files (x86)\SimHub\PluginsData\StatsPlus\StatsPlus.laps.db" `
  --target "C:\Program Files (x86)\SimHub\PluginsData\StatsPlus\StatsPlus.laps.ldb"
```

If the target already exists and should be replaced:

```powershell
.\StatsPlus.Migration\bin\Debug\net48\StatsPlus.Migration.exe `
  --source "C:\Program Files (x86)\SimHub\PluginsData\StatsPlus\StatsPlus.laps.db" `
  --target "C:\Program Files (x86)\SimHub\PluginsData\StatsPlus\StatsPlus.laps.ldb" `
  --overwrite
```

Close SimHub before migrating. Back up `StatsPlus.laps.db` first.

The migration utility:

- Reads the SQLite `games`, `cars`, `tracks`, `track_contexts`, and `laps` tables.
- Collapses each game/car/track/context combination into one `trackHistories` document.
- Writes each SQLite lap row into `laps`.
- Preserves lap ids, lap counts, validity flags, lap/sector times, timestamps, names, normalized names, and display-name fields.
- Recomputes `LastUpdatedUtc` from the newest migrated lap timestamp.
- Writes to a temporary `.tmp` LiteDB file before promoting it to the target.
- Leaves the source SQLite database untouched.
- Refuses to overwrite an existing target unless `--overwrite` is supplied.

Important: regenerate the `.ldb` with the current migration utility if an older test run created it with LiteDB 5. The runtime plugin now uses SimHub-compatible LiteDB 4.1.4.

## Build And Deploy

Build without copying into SimHub:

```powershell
dotnet build StatsPlus.sln /p:SimHubInstallPath=C:\does-not-exist
```

Run tests:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj
```

Deploy to the local SimHub install:

```powershell
dotnet build StatsPlus\StatsPlus.csproj
```

Close SimHub before deploying. If SimHub is running, `StatsPlus.dll` and `StatsPlus.pdb` will be locked and the copy step will fail.

After deploying, verify SimHub still has its bundled LiteDB:

```powershell
[Reflection.AssemblyName]::GetAssemblyName('C:\Program Files (x86)\SimHub\LiteDB.dll').Version
```

Expected:

```text
4.1.4.0
```

## What Changed

- Replaced runtime SQLite lap-history storage with LiteDB.
- Removed runtime JSON lap-history support.
- Kept settings JSON unchanged.
- Added `StatsPlusLiteDbRepository`.
- Added LiteDB document models for `trackHistories` and `laps`.
- Added `StatsPlus.Migration` for one-time SQLite-to-LiteDB conversion.
- Updated tests for LiteDB repository behavior, plugin storage behavior, and migration behavior.
- Added a `Test` configuration with SimHub SDK stubs so tests can run without a local SimHub SDK reference path.
- Changed runtime LiteDB package from `5.0.21` to `4.1.4` to match SimHub's built-in dependency.
- Updated deploy copy rules so StatsPlus does not overwrite SimHub's shared `LiteDB.dll` or `System.Buffers.dll`.
- Changed timestamp persistence to UTC ticks to avoid LiteDB 4 local-time conversion.

## What We Learned

- LiteDB is a document database, so mirroring the old normalized SQLite schema was not the best fit.
- Keeping `trackHistories` and `laps` separate is better than embedding unbounded lap arrays in track documents.
- In a SimHub plugin, dependency compatibility matters more than normal NuGet freshness because all plugins share the host process and root assembly load context.
- Copying a plugin dependency into the SimHub root can break built-in SimHub plugins if they depend on a different assembly version.
- The migration utility must target the same LiteDB major version as runtime, otherwise the plugin may not be able to open the migrated file.
- SimHub's built-in Statistics plugin is a useful smoke test after deployment because it exercises SimHub's own LiteDB dependency.

## Current Caveats

- LiteDB `4.1.4` raises a NuGet vulnerability warning. This is accepted for now because SimHub itself ships and loads that version.
- LiteDB 4 does not expose the same transaction API used by LiteDB 5, so runtime multi-document operations are best-effort rather than wrapped in LiteDB 5-style transactions.
- The migration utility protects ordinary source/target path mistakes, but filesystem aliases such as hard links or junctions should still be avoided. Keep a source database backup before migrating.
