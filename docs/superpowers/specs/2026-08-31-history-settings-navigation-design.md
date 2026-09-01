# StatsPlus History And Settings Navigation Design

## Summary

Split the StatsPlus settings control into two stable top-level tabs: `History` and `Settings`. Stored-game history tabs move inside the History view, while the Settings view keeps general plugin options, combined per-game options, data management, and About.

This removes the current competition between dynamic game tabs and the Settings tab. Users should always know where to go for browsing lap history and where to go for configuration or destructive data actions.

## Goals

- Replace dynamic top-level game tabs with stable `History` and `Settings` tabs.
- Keep each stored game's independent search state.
- Keep Stored History and Recorded Laps behavior unchanged inside the selected game.
- Keep Settings easy to find regardless of how many games have history.
- Preserve the new safer Data Management section with per-game `Clear history` rows.
- Keep changes within the existing WPF/SimHub stack without new dependencies.

## Non-Goals

- Do not change LiteDB schema or lap persistence.
- Do not change lap capture, personal best calculation, published SimHub property names, or diagnostic log content.
- Do not add cross-game search.
- Do not add a new visual style library.
- Do not replace the existing SimHub `SHTabControl` unless required by compatibility.

## Navigation Model

StatsPlus will expose two stable top-level tab view models:

- `History`
- `Settings`

`TopLevelTabs` will always contain these two tabs in that order. `Settings` no longer appears after dynamic game tabs.

The `History` tab owns the existing `GameHistoryTabs` collection. Its content template displays a nested SimHub tab control bound to those game tabs. If there is no stored history, the History tab still exists and shows an empty-state message.

The `Settings` tab keeps its current settings content and remains available even when no history exists.

## Selection Behavior

The plugin should track two concepts separately:

- selected top-level tab: `History` or `Settings`
- selected game-history tab: one item from `GameHistoryTabs`

Switching to Settings must not clear the selected game-history tab. This matters because users may configure settings or clear a game and return to History without losing their place.

When history refreshes:

- Existing game search state is preserved by game name.
- Existing selected game is preserved by game name when the game still exists.
- If the selected game was deleted or has no history after refresh, select the first remaining game.
- If no games remain, leave selected game as null and keep the stable top-level tab selection where possible.

## History Layout

The first implementation keeps the existing per-game history layout:

- Stored History section
- Search/filter toolbar
- summary grid
- Recorded Laps section
- selected-track caption
- validity toggle
- lap grid

This keeps the larger navigation change understandable and low-risk. The grids may retain the recent spacing improvements from the bounded UI pass.

## Settings Layout

Settings keeps:

- General
- Game Options
- Data Management
- About

Game Options remains the combined per-game table with `Record laps` and `Debug logs` columns. Data Management remains row-based, with one `Clear history` action per stored game and a separate `Clear all data` action.

## Error Handling

All existing repository failure handling remains in place. The navigation split should not introduce new exceptions when no history exists, when history is cleared, or when a previously selected game disappears.

## Testing

Focused tests should cover:

- Top-level tabs are always `History` and `Settings`.
- With history, `History` contains the game tabs rather than top-level game tabs.
- With no history, `History` still exists and selected game history is null.
- Selecting Settings does not clear selected game history.
- Refresh preserves selected game by name when possible.
- Clearing a named game while Settings is selected removes only that game.
- XAML binds the top-level tab control to stable top-level tabs and the History template nests the game tab control.
- Existing history search, filtering, lap selection, settings, and data-management tests continue to pass.

Standard verification remains:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj
dotnet build StatsPlus\StatsPlus.csproj /p:SimHubInstallPath=C:\does-not-exist
```

## Acceptance Criteria

- The visible top-level tabs are `History` and `Settings`.
- Stored games no longer appear as top-level tabs.
- Inside History, stored games remain selectable by game name.
- Settings remains available in the same position regardless of stored game count.
- Search state remains independent per game.
- Switching between History and Settings does not lose the selected game history tab.
- Per-game data clearing still works from Settings without a game dropdown.
- Full automated tests and no-deploy build pass with only the known LiteDB warning.
