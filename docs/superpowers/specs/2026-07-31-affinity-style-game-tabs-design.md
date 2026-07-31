# Affinity-Style Game Tabs Design

## Summary

Redesign the StatsPlus settings UI to feel like a sibling of the Affinity SimHub plugin while preserving the current StatsPlus lap-history workflow. The new layout will use one top-level tab per recorded game, followed by a Settings tab at the end. There will be no Overview tab.

A slim live status bar will sit above the scrollable tab content so current recording state remains visible from every tab.

## Goals

- Replace the fixed `Settings` and `Laps` top-level tabs with dynamic top-level game tabs plus a final `Settings` tab.
- Keep the current per-game history workflow: one grid for all saved car/track combinations and one grid for recorded lap times for the selected combination.
- Move existing settings, enabled-game toggles, and about text into the final `Settings` tab.
- Add an Affinity-style live status bar above the scrollable content.
- Use Affinity's visual language where it fits StatsPlus: compact sections, dark metric/status panels, cyan accent borders, and data-dense grids.
- Preserve existing lap-history operations: refresh history, clear selected game, clear all data, and toggle selected lap validity.

## Non-Goals

- Do not add an Overview tab.
- Do not redesign telemetry capture or lap persistence.
- Do not add new lap filtering, sorting, searching, or graphing behavior in this pass.
- Do not change the meaning of published SimHub properties.
- Do not add game logos.
- Do not split `StatsPlusPlugin` into broader architectural layers solely for this UI change.

## Layout

The root `SettingsControl` will use a two-row outer layout:

1. A fixed live status bar.
2. A scrollable content area containing the top-level `SHTabControl`.

The live status bar must stay outside and above the `ScrollViewer`. Scrolling the game/settings content must not scroll the live status bar out of view.

The top-level tab order will be:

```text
[Recorded game tab] [Recorded game tab] ... [Settings]
```

When there is no recorded game history yet, the tab control will show only `Settings`. Do not add a placeholder game tab.

## Live Status Bar

The live status bar will be a compact horizontal strip styled like Affinity's dark summary panels:

- Dark background: `#252525`.
- Cyan accent border: `#2CB7F0`.
- Small uppercase or semibold labels.
- Bold values where space allows.
- Text trimming/wrapping chosen so long game, track, car, or database values do not overlap.

The live state indicator will follow Affinity's state-driven color behavior:

- Add `IsTelemetryActive`, `LiveStatusLabel`, and `StatusSectionForeground` properties to `StatsPlusPlugin`, mirroring Affinity's pattern.
- `LiveStatusLabel` returns `Recording` when `IsTelemetryActive` is `true`; otherwise it returns `Standby`.
- `StatusSectionForeground` returns `Brushes.LimeGreen` when `IsTelemetryActive` is `true`; otherwise it returns `Brushes.Red`.
- The live status label and main `DataStatus` text bind to `StatusSectionForeground`.
- Supporting metrics keep the neutral StatsPlus/Affinity text colors so the active/inactive state is clear without turning the whole bar green or red.

`IsTelemetryActive` will be `true` when StatsPlus is actively processing supported, enabled, running telemetry for a reliable game/car/track context. It will stay `true` for short-lived active telemetry states such as pending lap capture or telemetry sync around a lap boundary.

`IsTelemetryActive` will be `false` when the plugin is disabled, the game is not running, telemetry data is unavailable, the current game is disabled or unsupported, persistence initialization has failed in a way that prevents normal recording, or the plugin is otherwise waiting for usable telemetry.

The bar will display the current live session state:

- `LiveStatusLabel`
- `DataStatus`
- `CurrentContext`
- `SessionLapCount`
- `LastLapSeconds`
- `SessionBestLapSeconds`
- `AllTimeBestLapSeconds`

`TimeSpanSecondsFormatter` will continue to format lap time values. Empty or zero values should render consistently with existing StatsPlus behavior.

The status bar is informational only. It does not own persistence, selection, or lap mutations.

## Game Tabs

Each recorded game gets one top-level tab. The tab header is the game name, matching the current `GameHistoryTab.GameName` behavior.

Each game tab will keep the current two-grid workflow:

- Top grid: all saved car/track combinations for that game.
- Bottom grid: recorded lap times for the selected car/track combination.

The top grid will bind to the selected game tab's track summaries. It will show the current columns with minor width adjustments allowed only to prevent clipping:

- Car
- Track
- Variation
- Laps
- Best
- Last UTC

Selecting a row in the top grid updates `SelectedTrackSummary` on the plugin and reloads `SelectedTrackLaps`, preserving the current behavior.

The bottom grid will bind to `SelectedTrackLaps` and keep the current recorded-lap columns:

- Lap
- Time
- S1
- S2
- S3
- Valid
- Recorded UTC

The selected lap row will continue to drive `SelectedLap` and `HasSelectedLap`.

## Game Tab Actions

Actions should remain close to their context:

- `Refresh history` belongs in a compact toolbar above the top grid.
- `Clear selected game` is enabled when a game tab is selected and clears that game's persisted data.
- `Clear all data` remains available but should be visually separated from routine actions because it is broader and destructive.
- `Toggle selected lap valid` belongs near the recorded-laps grid and is enabled only when a lap is selected.

Existing button handlers will stay in `SettingsControl.xaml.cs`, matching the current StatsPlus and Affinity code-behind pattern.

## Settings Tab

The final top-level tab will contain the existing settings content:

- General settings:
  - Enable plugin
  - Publish game / track / car
  - Custom label
  - Save settings
  - Reset defaults
- Enabled games toggles
- About text and published-property notes

The Settings tab should use Affinity-like spacing and `SHSection` composition, but it does not need new settings behavior.

## View Model Shape

StatsPlus should move from fixed `SHTabItem` declarations to a bound top-level tab collection similar to Affinity's pattern.

Extend the existing `GameHistoryTab` UI model and add one small settings-tab model in the StatsPlus namespace:

- `GameHistoryTab`
  - Represents one recorded game tab.
  - Exposes `Header` as the game name.
  - Contains the existing list of `StoredTrackSummary` rows for that game.
- `StatsPlusSettingsTab`
  - Represents the final Settings tab.
  - Exposes `Header` as `Settings`.

The plugin should expose:

- `ObservableCollection<object> TopLevelTabs`
- `object SelectedTopLevelTab`
- `GameHistoryTab SelectedGameHistoryTab`

Refreshing stored track summaries should rebuild or update the tab collection while preserving selection by game name when possible. If the selected game no longer exists after a clear/delete operation, selection should move to the first remaining game tab, or to `Settings` when no game tabs remain.

## Data Flow

Startup initializes settings and lap storage as it does today, then refreshes stored track summaries.

Refreshing summaries will:

1. Query `StatsPlusLiteDbRepository.GetTrackSummaries()`.
2. Group summaries by `GameName`.
3. Populate dynamic game tabs in game-name order.
4. Append the single Settings tab.
5. Preserve `SelectedTopLevelTab` by game name when possible.
6. Preserve or clear `SelectedTrackSummary` according to whether the previously selected game/car/track still exists.

Selecting a top grid row sets `SelectedTrackSummary` and reloads `SelectedTrackLaps`, as it does today.

Toggling lap validity reloads the selected track's laps and refreshes any affected summary values.

Clearing selected game data removes that game tab after refresh. Clearing all data removes all game tabs and leaves Settings selected.

## Error Handling And Empty States

When persistence is unavailable, the UI should still load and show Settings plus the live status bar. Game tabs should be empty or absent, and history actions should safely no-op through existing repository guards.

When no track row is selected, the recorded-laps area should show the existing selected-track caption behavior: `Select a track row above to inspect recorded laps.`

When a selected game has no visible track summaries after a refresh, the game tab should show a concise empty state instead of an empty-looking broken grid.

Long game, car, track, and database text must not overlap other controls.

## Testing

Add or update focused tests around the UI model behavior:

- New plugin initializes the top-level tab collection with a Settings tab.
- Refreshing history with two recorded games creates two game tabs followed by Settings.
- Refreshing history preserves the selected game tab when the game still exists.
- Refreshing history selects Settings when the previously selected game was deleted and no games remain.
- Selecting a stored track summary still loads recorded laps.
- Clearing selected game data clears the current game and refreshes tab selection safely.
- Live status bar bindings use existing plugin properties and do not require separate state.

Existing lap capture, LiteDB repository, migration, and settings tests should continue passing.

## Acceptance Criteria

- The top-level StatsPlus UI has no Overview tab.
- The top-level StatsPlus UI shows one tab per recorded game and a final Settings tab.
- Settings are no longer the first top-level tab when recorded game history exists.
- The live status bar is rendered above the scrollable content and remains visible while tab content scrolls.
- The live status label and `DataStatus` are green while StatsPlus is actively recording telemetry and red while StatsPlus is standby, disabled, unsupported, or waiting for usable telemetry.
- Each game tab contains a car/track summary grid and a recorded-laps grid for the selected combination.
- Existing history actions still work from the redesigned UI.
- Existing settings behavior still works from the final Settings tab.
- Automated tests cover dynamic tab creation, tab selection preservation, and affected history selection behavior.
