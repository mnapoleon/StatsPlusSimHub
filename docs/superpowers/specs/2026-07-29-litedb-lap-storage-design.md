# LiteDB Lap Storage Design

## Summary

Move StatsPlus lap-history persistence from SQLite to LiteDB. Runtime lap history will be LiteDB-only, stored in `StatsPlus.laps.ldb` under the existing `PluginsData\StatsPlus` folder. `StatsPlus.settings.json` remains JSON because it stores plugin settings, not lap history.

The plugin will not read, write, import, or fall back to `StatsPlus.laps.json`. Existing SQLite lap data will be moved once by a separate console migration utility, not by plugin startup code.

## Goals

- Replace `System.Data.SQLite.Core` usage in the main plugin with `LiteDB`.
- Preserve existing lap-history behavior: recording laps, querying best laps, listing summaries, listing selected-track laps, toggling validity, clearing a game, clearing all data, and publishing personal-best properties.
- Keep the runtime plugin free of SQLite migration logic.
- Provide a one-time SQLite-to-LiteDB migration utility for the current user data.
- Remove JSON lap-history persistence and fallback behavior from the plugin.

## Non-Goals

- Do not migrate `StatsPlus.settings.json`; settings stay JSON.
- Do not support automatic SQLite-to-LiteDB migration during plugin startup.
- Do not support automatic `StatsPlus.laps.json` import.
- Do not add UI for editing display-name overrides.
- Do not redesign telemetry capture or settings UI.

## Runtime Storage

The plugin stores lap history in:

```text
PluginsData\StatsPlus\StatsPlus.laps.ldb
```

Startup initializes the LiteDB repository and creates indexes if needed. If the LiteDB file does not exist, the repository creates an empty store. If initialization fails, the plugin logs the error and continues without persistent lap-history storage for that session. It does not switch to JSON lap-history storage.

`StatsPlus.DataFilePath` should publish the LiteDB path.

## LiteDB Data Model

Use a document-oriented structure instead of mirroring SQLite's normalized table shape:

- `trackHistories`
- `laps`

Each document uses a `long Id`. Natural identities stay normalized for case-insensitive lookup while preserving original display/base values.

### Track History Document

- `Id`
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

Indexes:

- `NormalizedGameName`
- `NormalizedModelName`
- `NormalizedTrackNameWithConfig`

The tuple `(NormalizedGameName, NormalizedModelName, NormalizedTrackNameWithConfig)` is unique by repository logic. LiteDB supports indexes on individual fields; the repository must query by all three normalized fields before inserting to enforce this natural identity.

### Lap Document

- `Id`
- `TrackHistoryId`
- `LapNumber`
- `LapTimeSeconds`
- `Sector1Seconds`
- `Sector2Seconds`
- `Sector3Seconds`
- `IsValid`
- `TimestampUtc`

Indexes:

- `TrackHistoryId`
- `TimestampUtc`
- `IsValid`
- `LapTimeSeconds`

Do not embed laps inside `trackHistories`. A track history can grow without bound, and lap validity toggles should update one small lap document instead of rewriting a large parent document.

## Repository API

Replace `StatsPlusSqliteRepository` with `StatsPlusLiteDbRepository`. Keep the public behavior and call surface aligned with the existing repository:

- `Initialize()`
- `HasLapData()`
- `AddLap(string gameName, string carModel, string trackName, string trackNameWithConfig, RecordedLap lap)`
- `ToggleLapValidity(long lapId)`
- `DeleteGameData(string gameName)`
- `ClearAllData()`
- `GetBestLapSeconds(string gameName, string carModel, string trackNameWithConfig)`
- `GetTrackSummaries()`
- `GetTrackLaps(string gameName, string carModel, string trackNameWithConfig)`
- `GetPersonalBestPropertyValues()`
- `TryGetCarDisplayName(string gameName, string carModel)`
- `Dispose()`

Do not include `ImportLegacyDatabase()` in the runtime repository, because lap-history JSON import is no longer supported.

## Plugin Changes

Rename SQLite-specific fields and constants to LiteDB-specific names:

- `SqliteDataFileName` becomes a LiteDB data file constant with value `StatsPlus.laps.ldb`.
- `_sqliteRepository` becomes `_liteDbRepository`.
- `UseSqlite` becomes a persistence availability check such as `UseLiteDb` or `HasLapRepository`.

Remove runtime JSON lap-history behavior:

- Remove `LegacyDataFileName`.
- Remove `_legacyDatabasePath`.
- Remove `_database` as persistent state.
- Remove `LoadDatabase()`, `SaveDatabase()`, `NormalizeLoadedDatabase()`, and JSON fallback branches for lap history.
- Remove `ResolveLegacyDataPath()` unless no tests or callers remain.

`LapDatabase`, `GameBucket`, `CarBucket`, `TrackBucket`, `RecordedLap`, `StoredTrackSummary`, `RecordedLapView`, and `GameHistoryTab` may remain if useful as data transfer or UI binding types. They must not imply JSON lap-history persistence.

When no repository is available because initialization failed, lap-history operations should no-op safely, clear in-memory UI collections as needed, and log the initialization failure once. Live telemetry properties can still update for the current session, but all-time/history values should return `0.0` when persistence is unavailable.

## Display Names

Keep the current display fallback behavior:

1. Use `DisplayModelName` or `DisplayTrackNameWithConfig` from LiteDB when non-empty.
2. For Assetto Corsa track display names, use the existing `ac_track_id_map.json` lookup.
3. Fall back to stored model or track names.

No display-name editing UI is added.

## One-Time Migration Utility

Add a console project, for example `StatsPlus.Migration`, outside the plugin runtime path.

The utility reads an existing SQLite file and writes a LiteDB file:

```text
StatsPlus.Migration.exe --source "...\StatsPlus.laps.db" --target "...\StatsPlus.laps.ldb"
```

Behavior:

- Reads the SQLite schema currently used by `StatsPlusSqliteRepository`.
- Collapses each SQLite `games` + `cars` + `tracks` + `track_contexts` row set into one LiteDB `trackHistories` document.
- Writes each SQLite `laps` row as one LiteDB `laps` document referencing the migrated `TrackHistoryId`.
- Preserves SQLite lap ids as LiteDB lap ids when possible so any future debugging can line up source and target rows.
- Creates new LiteDB `trackHistories` ids and keeps an in-memory map from SQLite `track_contexts.id` to LiteDB `trackHistories.Id` while migrating laps.
- Preserves lap counts, validity flags, lap times, sector times, timestamps, created timestamps, last-updated timestamps, original names, normalized names, and display-name columns.
- Refuses to overwrite an existing target LiteDB file unless an explicit overwrite flag is supplied.
- Leaves the source SQLite file untouched.
- Writes to a temporary target file first, then moves it into place after a successful migration.
- Prints source row counts and target document counts for `trackHistories` and `laps`.

SQLite package references are allowed in the migration project only.

## Error Handling

Runtime plugin:

- Logs LiteDB initialization failures.
- Disables persistent lap-history reads and writes for that session when initialization fails.
- Does not create or update `StatsPlus.laps.json`.
- Does not attempt SQLite or JSON migration.

Migration utility:

- Fails with a non-zero exit code when the source SQLite file is missing or unreadable.
- Fails with a non-zero exit code when the target LiteDB file exists and overwrite is not requested.
- Deletes the temporary target file after a failed migration.
- Does not modify or delete the source SQLite file.

## Testing

Repository tests should be adapted from the existing SQLite repository tests and run against LiteDB:

- Import-free initialization creates an empty store.
- `AddLap` uses case-insensitive identity and does not duplicate track histories.
- `ToggleLapValidity` updates the targeted lap and recalculates best laps.
- `DeleteGameData` removes only the selected game's track histories and laps.
- `ClearAllData` removes all lap-history data.
- `GetTrackSummaries` returns lap counts, best valid lap, and last recorded timestamps.
- `GetTrackLaps` returns selected-track laps in descending timestamp order.
- `GetPersonalBestPropertyValues` returns the expected SimHub property names and values.

Plugin storage tests should be updated for `StatsPlus.laps.ldb` and should remove assertions about `StatsPlus.laps.json`.

Migration utility tests should create a representative SQLite database with multiple games, cars, tracks, contexts, and laps, run the utility logic, and verify the LiteDB repository returns equivalent summaries, laps, validity, personal bests, and timestamps from the collapsed `trackHistories` + `laps` structure.

## Acceptance Criteria

- The main plugin project no longer references `System.Data.SQLite.Core`.
- The main plugin project references `LiteDB`.
- The test project references `LiteDB` and only references SQLite if needed for migration utility tests.
- Runtime lap-history persistence uses `StatsPlus.laps.ldb`.
- The plugin contains no runtime path that reads, writes, imports, or falls back to `StatsPlus.laps.json`.
- The one-time migration utility can convert the current SQLite database to LiteDB without modifying the SQLite source.
- Existing lap-history behavior is preserved by automated tests.
