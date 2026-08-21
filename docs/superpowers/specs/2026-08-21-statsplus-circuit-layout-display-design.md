# StatsPlus Circuit Layout Display Design

## Goal

StatsPlus should display stored lap history tracks using the same circuit name and circuit layout paradigm used by the sibling Affinity plugin. The change is display-only: stored identities, LiteDB documents, personal-best property names, and lap lookup keys stay based on the raw game/car/TrackNameWithConfig values already captured today.

## Affinity Pattern To Mirror

Affinity resolves a track display name first, then derives two display fields for track rows:

- `CircuitNameDisplay`
- `CircuitLayoutDisplay`

The raw track identity remains `TrackNameWithConfig`. The UI binds the track table to `CircuitNameDisplay` and `CircuitLayoutDisplay` with headers `Circuit Name` and `Circuit Layout`. Affinity also keeps `TrackDisplayName` available so future filters can search the complete display text in addition to the split fields.

The display rules in Affinity are:

- Assetto Corsa classic, Assetto Corsa Competizione, and LMU use the same already-resolved display value in both circuit columns without replacing underscores. StatsPlus should recognize both `LMU` and `Le Mans Ultimate` as LMU for this rule.
- Assetto Corsa EVO keeps StatsPlus' existing Assetto track-map display resolution, but uses the generic circuit/layout split rule after display resolution.
- rFactor 2 splits display text on `--`.
- Other games split display text on `-`.
- Underscores become spaces only in split display parts.
- iRacing circuit names are title-cased after splitting, preserving `GP`.
- Missing layout text becomes an empty string rather than changing the storage key.

## StatsPlus Current Shape

StatsPlus stores lap history in LiteDB under `PluginsData\StatsPlus\StatsPlus.laps.ldb`. `TrackHistoryDocument` stores raw and normalized identity fields:

- `GameName`
- `CarModel`
- `RawTrackName`
- `TrackNameWithConfig`
- `NormalizedTrackNameWithConfig`
- optional display fields persisted from older migration paths

`StatsPlusLiteDbRepository.GetTrackSummaries()` returns `StoredTrackSummary` rows. `StatsPlusPlugin.BuildTrackSummaries()` resolves car and track display names before grouping them into `GameHistoryTab.Tracks`. The WPF settings UI currently displays `Car`, `Track`, and `Variation`.

## Game Profile Architecture

StatsPlus should introduce a small game-profile registry instead of adding more direct `IsXGame` branches to `StatsPlusPlugin`. The registry resolves an `IStatsPlusGameProfile` for a `gameName` at live context changes and for each stored-history row during display preparation.

The first implementation should stay deliberately small. Profiles own game-specific answers that already exist or are needed for this display change:

- normalized game matching and display/debug settings keys
- recording-toggle lookup against `PluginSettings`
- track display-name resolution
- circuit name/layout display derivation
- sector-layout inference where a game needs special handling

Use a `GenericStatsPlusGameProfile` fallback for unknown or unsupported names. Do not introduce a dependency-injection container; a static/default registry instance is enough for this plugin and keeps tests simple.

## Design

Add game profiles in StatsPlus that mirror Affinity's circuit/layout split rules. Apply them after the existing display-name resolution in `StatsPlusPlugin.BuildTrackSummaries()` and `LoadSelectedTrackLaps()`.

`StoredTrackSummary` and `RecordedLapView` should gain:

- `CircuitNameDisplay`
- `CircuitLayoutDisplay`

For future filtering, keep the full resolved display value in `TrackNameWithConfigDisplay` and expose the split display fields on both summary rows and lap rows. These display fields are fuzzy/search surfaces, not exact storage identities. This gives a later filter implementation clear display targets:

- an Affinity-style track search can match `TrackNameWithConfigDisplay`, `CircuitNameDisplay`, and `CircuitLayoutDisplay`
- car search can match `CarModelDisplay` and `CarModel`

Exact future filters should keep querying by raw identity fields: `GameName`, `CarModel`, `RawTrackName`, and `TrackNameWithConfig`. If StatsPlus later needs exact layout filtering instead of display search, introduce a separate transient raw layout key at that time rather than treating `CircuitLayoutDisplay` as a durable identity.

Do not add filter controls in this change. The only visible UI change is replacing the stored-history `Track` and `Variation` columns with `Circuit Name` and `Circuit Layout`.

## Non-Goals

- Do not migrate existing LiteDB documents.
- Do not change `TrackHistoryDocument` identity fields or indexes.
- Do not change personal-best property names.
- Do not change lap capture behavior.
- Do not move every game-specific branch in the plugin in this change; migrate only the recording/debug key, track display, circuit display, and sector-layout behaviors needed by this work.
- Do not add filter UI, search boxes, persisted filters, or query parameters yet.

## Acceptance Criteria

- Stored history rows display `Circuit Name` and `Circuit Layout` columns in the StatsPlus settings tab.
- Assetto Corsa classic mapped display names appear in both circuit columns.
- Assetto Corsa classic fallback display names with underscores remain unchanged in both circuit columns.
- Assetto Corsa EVO keeps StatsPlus track-map display resolution but splits mapped display text on `-`.
- Assetto Corsa Competizione, `LMU`, and `Le Mans Ultimate` display the same track text in both circuit columns.
- rFactor 2 display text splits on `--`.
- Automobilista 2, RaceRoom, and similar hyphenated display text splits on `-`.
- iRacing circuit names are title-cased after splitting, preserving `GP`.
- StatsPlus game support checks route through the profile registry for the behaviors touched by this change.
- Raw track identity, selected-row lookup, all-time best lookup, and personal-best property names continue to use `TrackNameWithConfig`.
- Unit tests cover the circuit/layout split rules, prove selected history rows still load laps by raw identity even when display text collides, and guard personal-best property names against display-name leakage.

## Verification

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj
dotnet build StatsPlus\StatsPlus.csproj /p:SimHubInstallPath=C:\does-not-exist
```
