# StatsPlus SQLite Migration Plan

## Summary
Move lap-history persistence from `StatsPlus.laps.json` to a SQLite database while keeping `StatsPlus.settings.json` unchanged. Use a normalized schema with `games`, `cars`, `tracks`, `track_contexts`, and `laps`, plus a one-time automatic migration that imports existing JSON data on startup and preserves current behavior, including case-insensitive matching, Assetto Corsa display-name mapping, and validity toggling.

## Key Changes
- Add a SQLite-backed persistence layer as the single source of truth for lap history.
- Keep settings persistence in JSON; do not migrate plugin settings in this phase.
- Replace in-memory persisted storage (`LapDatabase` and nested buckets) with query-based reads/writes, while keeping existing UI/view model types if useful for binding.
- Add display override columns for future manual renaming:
  - `cars.display_model_name NULL`
  - `tracks.display_track_name_with_config NULL`
- Preserve current identity semantics from the JSON structure:
  - game identity by normalized game name
  - car identity by `(game_id, normalized_model_name)`
  - track identity by `(game_id, normalized_track_name_with_config)`
  - context identity by `(game_id, car_id, track_id)`

## Database Design
Create these tables:

- `games`
  - `id INTEGER PRIMARY KEY`
  - `name TEXT NOT NULL`
  - `normalized_name TEXT NOT NULL UNIQUE`

- `cars`
  - `id INTEGER PRIMARY KEY`
  - `game_id INTEGER NOT NULL`
  - `model_name TEXT NOT NULL`
  - `normalized_model_name TEXT NOT NULL`
  - `display_model_name TEXT NULL`
  - `UNIQUE(game_id, normalized_model_name)`
  - `FOREIGN KEY(game_id) REFERENCES games(id)`

- `tracks`
  - `id INTEGER PRIMARY KEY`
  - `game_id INTEGER NOT NULL`
  - `raw_track_name TEXT NOT NULL`
  - `track_name_with_config TEXT NOT NULL`
  - `normalized_track_name_with_config TEXT NOT NULL`
  - `display_track_name_with_config TEXT NULL`
  - `created_utc TEXT NOT NULL`
  - `last_updated_utc TEXT NOT NULL`
  - `UNIQUE(game_id, normalized_track_name_with_config)`
  - `FOREIGN KEY(game_id) REFERENCES games(id)`

- `track_contexts`
  - `id INTEGER PRIMARY KEY`
  - `game_id INTEGER NOT NULL`
  - `car_id INTEGER NOT NULL`
  - `track_id INTEGER NOT NULL`
  - `created_utc TEXT NOT NULL`
  - `last_updated_utc TEXT NOT NULL`
  - `UNIQUE(game_id, car_id, track_id)`
  - `FOREIGN KEY(game_id) REFERENCES games(id)`
  - `FOREIGN KEY(car_id) REFERENCES cars(id)`
  - `FOREIGN KEY(track_id) REFERENCES tracks(id)`

- `laps`
  - `id INTEGER PRIMARY KEY`
  - `track_context_id INTEGER NOT NULL`
  - `lap_number INTEGER NOT NULL`
  - `lap_time_seconds REAL NOT NULL`
  - `sector1_seconds REAL NOT NULL`
  - `sector2_seconds REAL NOT NULL`
  - `sector3_seconds REAL NOT NULL`
  - `is_valid INTEGER NOT NULL`
  - `timestamp_utc TEXT NOT NULL`
  - `FOREIGN KEY(track_context_id) REFERENCES track_contexts(id) ON DELETE CASCADE`

Create indexes on:
- `cars(game_id, normalized_model_name)`
- `tracks(game_id, normalized_track_name_with_config)`
- `track_contexts(game_id, car_id, track_id)`
- `laps(track_context_id, timestamp_utc DESC)`
- `laps(track_context_id, is_valid, lap_time_seconds)`

## Implementation Changes
- Add a new repository/service layer responsible for:
  - database initialization and schema creation
  - transactions
  - upsert/lookups for games, cars, tracks, and track contexts
  - lap inserts, lap validity updates, game deletes, track summary queries, selected-track lap queries, and best-lap queries
- Update plugin startup to:
  - compute the SQLite path, e.g. `StatsPlus.laps.db`
  - initialize schema
  - run one-time JSON migration if the DB is absent or empty and `StatsPlus.laps.json` exists
  - use SQLite for all subsequent reads and writes
- Replace current JSON-backed behaviors with repository calls:
  - `LoadDatabase()` / `SaveDatabase()`
  - `AddLapToDatabase(...)`
  - `GetBestLapSeconds(...)`
  - `BuildTrackSummaries()`
  - `LoadSelectedTrackLaps(...)`
  - `ToggleSelectedLapValidity()`
  - `ClearSelectedGameData()`
  - personal-best property generation
- Keep current display fallback order:
  1. DB override (`display_model_name` / `display_track_name_with_config`) if non-empty
  2. Assetto Corsa track map lookup for track display where applicable
  3. raw stored name
- Keep current case-insensitive behavior by using normalized columns for lookups and uniqueness, while preserving original casing in stored display/base fields.
- Introduce a real lap primary key and surface it in the selected-lap view model so validity toggles and future edits target rows directly instead of matching by timestamp and lap number.

## JSON Migration
On first startup with SQLite enabled:
- If the DB already contains schema and data, skip migration.
- If the DB is missing or empty and `StatsPlus.laps.json` exists:
  - load JSON using the existing model
  - run the existing sector normalization logic before import
  - import all data in a single transaction
  - upsert `games`, `cars`, `tracks`, and `track_contexts` by their natural keys
  - insert every lap row under its resolved `track_context_id`
  - preserve `created_utc` and `last_updated_utc` from track buckets
- After a successful migration:
  - rename `StatsPlus.laps.json` to `StatsPlus.laps.json.bak`
  - log a clear success message with source and destination paths
- If migration fails:
  - log the error
  - do not rename the JSON file
  - fall back to the existing JSON-backed path for that run so the plugin remains usable

## Test Plan
- Startup with no JSON and no DB creates an empty SQLite store and behaves normally.
- Startup with existing JSON and no DB migrates all data exactly once.
- Startup with existing DB does not re-import JSON.
- Mixed-case names in JSON do not create duplicate `games`, `cars`, or `tracks`.
- Inserting a lap updates:
  - the correct track context
  - `last_updated_utc`
  - summaries, best laps, and personal-best plugin properties
- Toggling lap validity updates the targeted lap row and recalculates best-lap values correctly.
- Clearing a game removes its contexts and laps without affecting other games.
- Selected-track lap history loads in descending timestamp order and still supports current UI actions.
- Assetto Corsa display names still resolve correctly when no manual override is present.
- Manual override fallback logic works once override columns are populated.
- Migration preserves lap counts, best laps, validity flags, timestamps, and sector values for representative JSON samples.

## Assumptions
- `StatsPlus.settings.json` remains unchanged in this phase.
- SQLite provider choice should favor the simplest reliable option for `net48` in the SimHub plugin environment; implementation should pick one and keep it isolated behind the repository layer.
- `track_name_with_config` remains the canonical track identity key; `raw_track_name` is descriptive metadata.
- `display_model_name` and `display_track_name_with_config` are nullable override fields only; no editing UI is added in this phase.
- The one-time migration should be automatic and silent aside from logging; no user prompt is required.
