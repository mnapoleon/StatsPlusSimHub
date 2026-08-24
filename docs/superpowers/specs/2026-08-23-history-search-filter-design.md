# StatsPlus History Search And Filter Design

## Summary

Add an incremental search control to each recorded-game tab so users can quickly find stored lap history by car, circuit name, or circuit layout. The control will combine one text box with a field selector that defaults to all searchable fields.

Search is scoped to the current game tab. It filters the car/circuit/layout summary grid; selecting a visible summary row continues to load that combination's recorded lap times in the existing Recorded Laps grid.

## Goals

- Make stored lap times easier to find when a game has many car, circuit, and layout combinations.
- Add one compact search box to every game-history tab.
- Search car, circuit name, and circuit layout by default.
- Let the user narrow matching to only car, circuit, or layout.
- Update results immediately as each character is typed.
- Support case-insensitive, partial-word matching such as `Nord` matching `Nordschleife`.
- Preserve the existing game-tab and two-grid history workflow.
- Keep search isolated from lap persistence, telemetry capture, and published SimHub properties.

## Non-Goals

- Do not search across games.
- Do not add a separate all-games results tab.
- Do not search lap duration, sector duration, lap number, validity, or recorded timestamp.
- Do not change the LiteDB schema or migrate existing data.
- Do not persist search state across SimHub restarts.
- Do not add wildcard, regular-expression, or advanced query syntax.
- Do not change how personal bests are calculated or published.

## Search Controls

Each `GameHistoryTab` will display a compact search row immediately above its Stored History grid:

```text
[All fields v] [Search car, circuit, or layout...] [Clear]
```

The field selector will provide these choices in this order:

1. `All fields`
2. `Car`
3. `Circuit`
4. `Layout`

`All fields` is the default. The search text box will update its source on every keystroke. No Enter key or explicit Search button is required.

The Clear button will empty the search text, reset the selector to `All fields`, and restore every summary row for that game.

Each game tab owns independent search state. Switching between game tabs preserves the text and selected field for each tab for the rest of the current SimHub session.

## Searchable Values

Search will operate on the same display values shown in the Stored History grid:

- Car: `StoredTrackSummary.CarModelDisplay`
- Circuit: `StoredTrackSummary.CircuitNameDisplay`
- Layout: `StoredTrackSummary.CircuitLayoutDisplay`

Raw telemetry identifiers are not searched when they differ from the displayed value. This ensures users can search for the text they can see.

Null, empty, or whitespace-only display values will be treated as empty strings and will not cause search failures.

## Matching Rules

Matching is case-insensitive and incremental. Every text change immediately recomputes the visible summary rows.

The search text will be trimmed and split into non-empty terms at whitespace boundaries. Each term uses substring matching, including while the term is incomplete. Examples include:

- `Nord` matches `Nordschleife`.
- `ring` matches `Nurburgring`.
- `BMW Nord` contains two partial terms.

When `All fields` is selected, every term must appear somewhere across the combined car, circuit, and layout display values. Different terms may match different fields. For example, `BMW Nord` matches when `BMW` appears in the car and `Nord` appears in the circuit or layout.

When `Car`, `Circuit`, or `Layout` is selected, every term must appear within the selected field's display value.

An empty or whitespace-only search matches every summary row regardless of the selected field.

Rows retain their existing order after filtering. Search does not introduce a new sort order.

## View-Model Shape

`GameHistoryTab` will remain the UI model for one recorded game and will gain responsibility for that tab's search state and visible summaries. It will expose:

- The complete set of track summaries for the game.
- The filtered set bound to the Stored History grid.
- Search text.
- The selected search field.
- The available search-field choices.
- A command or method used by the Clear button.

The search-field selection will use a small explicit value type rather than comparing user-facing strings throughout the filter logic. The display labels remain `All fields`, `Car`, `Circuit`, and `Layout`.

Search matching will be implemented as a deterministic, side-effect-free operation over a `StoredTrackSummary`. This keeps partial matching independently testable without constructing WPF controls or opening LiteDB.

The filtered list is a view of the already-loaded summaries. The repository remains the source of the full summary set.

## Data Flow

Startup and history refresh will continue to call `StatsPlusLiteDbRepository.GetTrackSummaries()` and group results into game tabs.

For each game tab:

1. The plugin supplies the complete summaries for that game.
2. The tab applies its current search text and selected field.
3. The Stored History grid binds to the resulting filtered summaries.
4. Typing or changing the selector reapplies the filter immediately.
5. Selecting a visible summary sets the existing plugin-level `SelectedTrackSummary`.
6. The plugin loads that combination's laps into the existing `SelectedTrackLaps` collection.

When a repository refresh rebuilds the dynamic game tabs, the plugin will preserve search text and selected field by game name before replacing the tab collection. The preserved state will be reapplied to the new summary data. A newly discovered game starts with blank search text and `All fields` selected.

Adding a lap, toggling validity, or manually refreshing history will therefore update the underlying summaries and then reapply the active search.

Clearing selected-game data removes that game's tab and its in-memory search state. Clearing all data removes every game tab and leaves the Settings tab, matching current behavior.

## Selection Behavior

If an active filter hides the currently selected summary row, `SelectedTrackSummary` and `SelectedLap` will be cleared and the Recorded Laps collection will be emptied. The existing selected-track caption will return to its prompt asking the user to select a row.

Clearing or broadening the filter restores matching summary rows but does not automatically reselect a previously hidden row. This avoids showing recorded laps for a row that is no longer visibly selected.

Changing the search on one game tab must not alter the filter state of another game tab.

## Empty And Error States

The UI will distinguish two empty states:

- If a game has stored summaries but the active filter matches none, show `No matching history.` near the summary grid.
- If no stored history exists for a game, retain the existing empty-history behavior rather than presenting it as a failed search.

The no-results message will disappear as soon as the incremental search produces at least one match or the search is cleared.

Repository and persistence failures will continue through the plugin's existing safe refresh behavior. Filtering an empty in-memory list is valid and returns no rows without raising a new error.

## Persistence And Compatibility

No settings fields will be added. Search text and selected field exist only in memory for the lifetime of the current plugin instance.

No LiteDB documents, indexes, or repository query signatures need to change. The existing `LapTimeSeconds` and `TimestampUtcTicks` indexes are unrelated to this metadata filter.

Telemetry processing, lap capture, validity toggling, personal-best calculations, and published SimHub properties remain unchanged.

The implementation must continue targeting the repository's existing .NET Framework and WPF versions and must not add a new package dependency.

## Testing

Focused unit tests will cover:

- Blank search returns every summary.
- `All fields` matches car, circuit, and layout display values.
- A specific field excludes matches found only in other fields.
- Matching is case-insensitive.
- Incomplete terms use substring matching, including `Nord` matching `Nordschleife`.
- Multiple terms use AND semantics.
- In `All fields`, multiple terms may match across different fields.
- Null or empty display values are handled safely.
- Filtering preserves the source row order.
- Clear empties the query, restores `All fields`, and restores all rows.
- Two game tabs retain independent search state.
- Refreshing summaries preserves search state by game name and reapplies it to new data.
- Filtering out the selected summary clears the selected summary, selected lap, and recorded-laps list.
- Existing track selection still loads recorded laps after filtering.

Focused XAML tests will verify that each game-tab template contains:

- The field selector.
- The incremental text binding.
- The Clear action.
- The filtered summary binding.
- The no-results message.

Standard verification remains:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj
dotnet build StatsPlus\StatsPlus.csproj /p:SimHubInstallPath=C:\does-not-exist
```

## Acceptance Criteria

- Every recorded-game tab shows one field selector, one search box, and one Clear action above Stored History.
- The selector defaults to `All fields` and offers `Car`, `Circuit`, and `Layout`.
- Typing filters the current game tab on every keystroke without requiring Enter.
- Partial input such as `Nord` matches any displayed searchable value containing that text, regardless of case.
- Multiple terms all have to match, and `All fields` permits the terms to match across different fields.
- Search affects only the current game tab's car/circuit/layout summary rows.
- Each game tab retains independent in-memory search state while switching tabs and after history refreshes.
- Filtering out the selected row clears the Recorded Laps details.
- Clear restores all summary rows and resets the selector to `All fields`.
- A filter with zero results shows `No matching history.`
- Search does not modify stored data, database structure, telemetry behavior, personal bests, or published properties.
- Existing automated tests continue to pass, and focused tests cover the new matching and selection behavior.
