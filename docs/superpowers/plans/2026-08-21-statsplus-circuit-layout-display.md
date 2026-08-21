# StatsPlus Circuit Layout Display Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Display StatsPlus stored lap history with Affinity-style `Circuit Name` and `Circuit Layout` columns while introducing a small per-game profile registry that reduces direct `IsXGame` branching for the behaviors touched by this change.

**Architecture:** Add `IStatsPlusGameProfile` implementations for supported games and a registry that resolves a profile from `gameName` at live context changes and per stored-history row. Profiles own recording-toggle lookup, debug settings keys/display names, track display mapping, circuit/layout display, and sector-layout inference; storage identity and personal-best keys remain raw.

**Tech Stack:** C#/.NET Framework plugin, WPF XAML settings UI, LiteDB, MSTest.

**Spec:** `docs/superpowers/specs/2026-08-21-statsplus-circuit-layout-display-design.md`

## Global Constraints

- Follow `.codex/project-practice.md`.
- Mirror proven Affinity plugin patterns for game naming and display behavior.
- Keep runtime storage under `PluginsData\StatsPlus`.
- Do not migrate LiteDB documents for this display-only change.
- Do not change personal-best property names or raw identity lookups.
- Do not introduce a dependency-injection container; use a lightweight registry.
- Do not move unrelated telemetry branches in this change.
- Use TDD where practical: write failing tests first, confirm red, then implement.
- Standard verification is `dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj` and `dotnet build StatsPlus\StatsPlus.csproj /p:SimHubInstallPath=C:\does-not-exist`.

---

## File Structure

- Create `StatsPlus/StatsPlusGameProfiles.cs`: game-profile interface, default registry, supported profile implementations, circuit display DTO, normalization helpers.
- Modify `StatsPlus/LapHistoryModels.cs`: add `CircuitNameDisplay` and `CircuitLayoutDisplay` to `StoredTrackSummary` and `RecordedLapView`.
- Modify `StatsPlus/StatsPlusPlugin.cs`: resolve game-specific recording/debug/display/sector behavior through the registry for the behaviors touched by this work.
- Modify `StatsPlus/SettingsControl.xaml`: replace stored-history `Track` and `Variation` columns with `Circuit Name` and `Circuit Layout`.
- Create `StatsPlus.Tests/StatsPlusGameProfileTests.cs`: direct tests for profile resolution, game aliases, display mapping, circuit/layout splitting, and sector-layout inference.
- Create `StatsPlus.Tests/SettingsControlXamlTests.cs`: static XAML tests for stored-history column headers and bindings.
- Modify `StatsPlus.Tests/StatsPlusPluginTabLayoutTests.cs`: integration tests proving display fields are populated without breaking selected-row lap loading or raw personal-best keys.
- Modify `README.md`: document display fields as fuzzy search surfaces and raw fields as lookup identity.

---

### Task 0: Prepare Implementation Branch Or Worktree

**Files:**
- No production files.

**Interfaces:**
- Produces: an implementation workspace that is not an unreviewed direct commit path on `main`.
- Consumes: current git branch and status.

- [ ] **Step 1: Check current repo state**

Run:

```powershell
git branch --show-current
git status --short
git log --oneline -5
```

Expected: identify whether the checkout is on `main`, whether local files are dirty, and what recent history the work will build on.

- [ ] **Step 2: Create or switch to an implementation branch**

If the checkout is on `main`, create a short descriptive feature branch without the `codex/` prefix unless the user explicitly asks for that prefix.

Expected: branch changes to a feature branch that is not `main`.

- [ ] **Step 3: Confirm branch and status**

Run:

```powershell
git branch --show-current
git status --short
```

Expected: branch is not `main` for implementation commits, and only intentional planning-doc changes are present before code edits begin.

---

### Task 1: Add Game Profile Registry And View Model Fields

**Files:**
- Create: `StatsPlus/StatsPlusGameProfiles.cs`
- Modify: `StatsPlus/LapHistoryModels.cs`
- Create: `StatsPlus.Tests/StatsPlusGameProfileTests.cs`

**Interfaces:**
- Produces: `internal sealed class CircuitDisplayParts` with `string CircuitNameDisplay` and `string CircuitLayoutDisplay`.
- Produces: `internal sealed class StatsPlusTrackDisplayContext`.
- Produces: `internal interface IStatsPlusGameProfile`.
- Produces: `internal sealed class StatsPlusGameProfileRegistry` with `static StatsPlusGameProfileRegistry CreateDefault()` and `IStatsPlusGameProfile Resolve(string gameName)`.
- Produces: `StoredTrackSummary.CircuitNameDisplay`, `StoredTrackSummary.CircuitLayoutDisplay`, `RecordedLapView.CircuitNameDisplay`, and `RecordedLapView.CircuitLayoutDisplay`.
- Preserves: public model shape except additive display properties.

- [ ] **Step 1: Write failing profile tests**

Create `StatsPlus.Tests/StatsPlusGameProfileTests.cs` covering:

- `Resolve_RecognizesSupportedGameAliasesAndSettingsKeys`
- `Resolve_UsesProfileRecordingToggles`
- `AssettoCorsaProfile_MapsClassicAndEvoTrackDisplaysOnly`
- `CircuitDisplay_DuplicatesSameDisplayGamesWithoutNormalizingUnderscores`
- `CircuitDisplay_SplitsGameSpecificLayouts`
- `InferSectorLayout_KeepsAssettoFamilyTwoSectorFallbackInProfile`

Required expectations:

- `Assetto Corsa`, `AssettoCorsa`, ACC, AC EVO, Automobilista 2, iRacing, LMU, Le Mans Ultimate, rFactor2, R3E/RRRE resolve to their canonical settings keys.
- Unknown games resolve to a generic profile with empty settings key and disabled recording.
- AC classic and AC EVO use the Assetto track display map; ACC leaves the raw display value alone.
- AC classic, ACC, and LMU duplicate the resolved display value into both circuit fields without underscore replacement.
- AC EVO maps first, then splits on `-`.
- rFactor2 splits on `--`.
- Other games split on `-`.
- iRacing title-cases circuit names while preserving `GP`.
- Missing layout becomes `string.Empty`.
- Assetto-family sector inference preserves the current two-sector fallback behavior.

- [ ] **Step 2: Run profile tests and confirm they fail**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter StatsPlusGameProfileTests
```

Expected: FAIL at compile time because `StatsPlusGameProfileRegistry`, `IStatsPlusGameProfile`, `StatsPlusTrackDisplayContext`, and `CircuitDisplayParts` do not exist yet.

- [ ] **Step 3: Add additive view model fields**

Modify `StatsPlus/LapHistoryModels.cs` by adding these properties to both `StoredTrackSummary` and `RecordedLapView` immediately after `TrackNameWithConfigDisplay`:

```csharp
public string CircuitNameDisplay { get; set; } = string.Empty;
public string CircuitLayoutDisplay { get; set; } = string.Empty;
```

- [ ] **Step 4: Add the profile registry implementation**

Create `StatsPlus/StatsPlusGameProfiles.cs` in the `StatsPlus` namespace.

Required types:

- `CircuitDisplayParts`
- `StatsPlusTrackDisplayContext`
- `IStatsPlusGameProfile`
- `StatsPlusGameProfileRegistry`
- `StatsPlusGameProfileBase`
- `GenericStatsPlusGameProfile`
- `StatsPlusGameName`
- concrete profiles for Assetto Corsa, Assetto Corsa Competizione, Assetto Corsa EVO, Automobilista 2, iRacing, LMU, rFactor2, and RaceRoom

Profile behavior:

- `StatsPlusGameName.Normalize` removes non-alphanumeric characters and lowercases.
- `GenericStatsPlusGameProfile` disables recording and uses generic split behavior.
- AC classic maps display names from `StatsPlusTrackDisplayContext.AssettoCorsaTrackMap`, duplicates circuit/layout, and uses Assetto-family sector inference.
- ACC duplicates circuit/layout and uses Assetto-family sector inference.
- AC EVO maps display names from the Assetto map, then uses generic split behavior, and uses Assetto-family sector inference.
- LMU duplicates circuit/layout.
- rFactor2 splits on `--`.
- iRacing splits on `-`, replaces underscores in split parts, title-cases circuit names, and preserves `GP`.
- Automobilista 2 and RaceRoom use the generic `-` split.

- [ ] **Step 5: Run profile tests and confirm they pass**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter StatsPlusGameProfileTests
```

Expected: PASS.

- [ ] **Step 6: Commit Task 1**

```powershell
git add StatsPlus\StatsPlusGameProfiles.cs StatsPlus\LapHistoryModels.cs StatsPlus.Tests\StatsPlusGameProfileTests.cs
git commit -m "Add StatsPlus game profiles"
```

---

### Task 2: Route Existing Game-Specific Plugin Decisions Through Profiles

**Files:**
- Modify: `StatsPlus/StatsPlusPlugin.cs`
- Modify: `StatsPlus.Tests/StatsPlusPluginLmuSupportTests.cs`
- Modify: `StatsPlus.Tests/StatsPlusDebugLoggingSettingsTests.cs`

**Interfaces:**
- Consumes: `StatsPlusGameProfileRegistry.CreateDefault()`, `Resolve(string gameName)`, `IStatsPlusGameProfile.IsRecordingEnabled`, `SettingsKey`, `DisplayName`, `GetTrackDisplayName`, `StatsPlusTrackDisplayContext`, and `InferSectorLayout`.
- Produces: plugin behavior equivalent to current branchy implementation, but routed through profiles for touched game checks.
- Preserves: existing public/private method names where tests reflect them.

- [ ] **Step 1: Add characterization tests for plugin methods that should delegate to profiles**

Add/extend tests to prove:

- `IsGameRecordingEnabled_RecognizesLeMansUltimateAlias`
- `EnsureGameDebugLoggingConfigured_UsesProfileSettingsKeyForAliases`

Expected: these tests characterize existing behavior and should pass before the refactor.

- [ ] **Step 2: Run focused characterization tests before refactor**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter "StatsPlusPluginLmuSupportTests|StatsPlusDebugLoggingSettingsTests"
```

Expected: PASS.

- [ ] **Step 3: Add registry field in `StatsPlusPlugin`**

```csharp
private readonly StatsPlusGameProfileRegistry _gameProfiles = StatsPlusGameProfileRegistry.CreateDefault();
```

- [ ] **Step 4: Replace recording toggle switch with profile lookup**

```csharp
private bool IsGameRecordingEnabled(string gameName)
{
    return _gameProfiles.Resolve(gameName).IsRecordingEnabled(Settings);
}
```

- [ ] **Step 5: Replace debug settings key/display helpers with profile lookup**

Route `GetDebugLoggingSettingsKey`, `GetDebugLoggingDisplayName`, and `IsSupportedDebugLoggingSettingsKey` through the resolved profile. Keep `DefaultGameDebugLoggingEntries` in the plugin for now to avoid widening this change into settings persistence reshaping.

- [ ] **Step 6: Replace track display helper with profile lookup**

```csharp
private string GetDisplayTrackNameWithConfig(string gameName, string rawTrackNameWithConfig)
{
    return _gameProfiles.Resolve(gameName).GetTrackDisplayName(
        rawTrackNameWithConfig,
        new StatsPlusTrackDisplayContext(_assettoCorsaTrackMap));
}
```

Remove `ShouldMapAssettoCorsaTrackName` if no remaining code uses it.

- [ ] **Step 7: Replace sector-layout game branch with profile lookup**

```csharp
private void InferSectorLayout(string gameName, double lapTime, ref double sector1, ref double sector2, ref double sector3)
{
    _gameProfiles.Resolve(gameName).InferSectorLayout(lapTime, ref sector1, ref sector2, ref sector3);
}
```

Remove `IsAssettoCorsaGame` only if no remaining code uses it. If `QueueLapCaptureIfNeeded` still needs `IsAssettoCorsaGame` for sector evidence, leave that branch for a later refactor unless you also add and test a profile property for that behavior.

- [ ] **Step 8: Run focused profile-routing tests**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter "StatsPlusGameProfileTests|StatsPlusPluginLmuSupportTests|StatsPlusDebugLoggingSettingsTests|StatsPlusPluginLapCaptureTests"
```

Expected: PASS.

- [ ] **Step 9: Commit Task 2**

```powershell
git add StatsPlus\StatsPlusPlugin.cs StatsPlus.Tests\StatsPlusPluginLmuSupportTests.cs StatsPlus.Tests\StatsPlusDebugLoggingSettingsTests.cs
git commit -m "Route StatsPlus game checks through profiles"
```

---

### Task 3: Populate Circuit Fields In Plugin View Models

**Files:**
- Modify: `StatsPlus/StatsPlusPlugin.cs`
- Modify: `StatsPlus.Tests/StatsPlusPluginTabLayoutTests.cs`

**Interfaces:**
- Consumes: `IStatsPlusGameProfile.GetCircuitDisplayParts(string trackDisplayName)`.
- Produces: populated `CircuitNameDisplay` and `CircuitLayoutDisplay` on every stored summary and selected lap row.
- Preserves: selected-lap loading by `GameName`, `CarModel`, and raw `TrackNameWithConfig`.

- [ ] **Step 1: Write failing integration test for summary display fields**

Add `RefreshStoredTrackSummaries_PopulatesAffinityStyleCircuitColumns`.

Required behavior:

- Seed an Automobilista 2 lap with raw `TrackNameWithConfig` such as `Buenos_Aires-Buenos_Aires_Circuito_15`.
- Summary keeps the raw value in `TrackNameWithConfig`.
- `CircuitNameDisplay` is `Buenos Aires`.
- `CircuitLayoutDisplay` is `Buenos Aires Circuito 15`.

- [ ] **Step 2: Write failing integration test for selected lap display fields and raw lookup**

Add `SelectedTrackSummary_LoadsRecordedLapsAndPopulatesCircuitDisplayWithoutChangingLookup`.

Required behavior:

- Seed an rFactor2 lap with `Lime Rock Park -- No Chicanes`.
- Selecting the summary loads the row by raw `TrackNameWithConfig`.
- Selected lap has `CircuitNameDisplay` `Lime Rock Park` and `CircuitLayoutDisplay` `No Chicanes`.

- [ ] **Step 3: Write failing integration tests for display collision and personal-best raw key preservation**

Add tests proving:

- Two iRacing rows whose display values collide still load selected laps by raw `TrackNameWithConfig`.
- Both rows can display `Spielberg GP` / `Grand Prix` while selecting distinct raw rows.
- Assetto Corsa personal-best properties continue to use raw `ks_brands_hatch_indy` identity instead of `Brands_Hatch_Indy`.

- [ ] **Step 4: Run integration tests and confirm they fail**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter StatsPlusPluginTabLayoutTests
```

Expected: FAIL because the new circuit fields remain empty.

- [ ] **Step 5: Add display-field applicators in `StatsPlusPlugin`**

Add private overloads that apply `CircuitDisplayParts` to `StoredTrackSummary` and `RecordedLapView` by resolving the row's `GameName` and splitting the already-resolved `TrackNameWithConfigDisplay`.

- [ ] **Step 6: Populate fields for stored summaries and lap rows**

After existing car/track display resolution:

- In `BuildTrackSummaries()`, call `ApplyCircuitDisplayFields(summary)`.
- In `LoadSelectedTrackLaps()`, call `ApplyCircuitDisplayFields(lapView)`.

- [ ] **Step 7: Run integration tests and confirm they pass**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter StatsPlusPluginTabLayoutTests
```

Expected: PASS.

- [ ] **Step 8: Commit Task 3**

```powershell
git add StatsPlus\StatsPlusPlugin.cs StatsPlus.Tests\StatsPlusPluginTabLayoutTests.cs
git commit -m "Populate StatsPlus circuit display fields"
```

---

### Task 4: Switch Stored-History UI Columns To Circuit Name And Layout

**Files:**
- Modify: `StatsPlus/SettingsControl.xaml`
- Create: `StatsPlus.Tests/SettingsControlXamlTests.cs`

**Interfaces:**
- Consumes: `StoredTrackSummary.CircuitNameDisplay` and `StoredTrackSummary.CircuitLayoutDisplay`.
- Produces: WPF stored-history grid columns labeled `Circuit Name` and `Circuit Layout`.
- Preserves: selected-row binding to `SelectedTrackSummary`.

- [ ] **Step 1: Write a failing static XAML test for stored-history columns**

Create `StatsPlus.Tests/SettingsControlXamlTests.cs`.

Required assertions:

- XAML contains `Header="Circuit Name" Binding="{Binding CircuitNameDisplay}"`.
- XAML contains `Header="Circuit Layout" Binding="{Binding CircuitLayoutDisplay}"`.
- XAML no longer contains the stored-history `Track` column bound to `TrackName`.
- XAML no longer contains the stored-history `Variation` column bound to `TrackNameWithConfigDisplay`.

- [ ] **Step 2: Run the XAML test and confirm it fails**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter SettingsControlXamlTests
```

Expected: FAIL because `SettingsControl.xaml` still has `Track` and `Variation` columns bound to the old display fields.

- [ ] **Step 3: Replace the stored-history columns**

Modify `StatsPlus/SettingsControl.xaml` in the stored-history grid:

```xml
<DataGrid.Columns>
    <DataGridTextColumn Header="Car" Binding="{Binding CarModelDisplay}" Width="180" />
    <DataGridTextColumn Header="Circuit Name" Binding="{Binding CircuitNameDisplay}" Width="180" />
    <DataGridTextColumn Header="Circuit Layout" Binding="{Binding CircuitLayoutDisplay}" Width="180" />
    <DataGridTextColumn Header="Laps" Binding="{Binding LapCount}" Width="60" />
    <DataGridTextColumn Header="Best" Binding="{Binding BestLapSeconds, Converter={StaticResource TimeSpanSecondsFormatter}}" Width="90" />
    <DataGridTextColumn Header="Last UTC" Binding="{Binding LastRecordedUtc, StringFormat={}{0:u}}" Width="150" />
</DataGrid.Columns>
```

- [ ] **Step 4: Run the XAML test and build**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter SettingsControlXamlTests
dotnet build StatsPlus\StatsPlus.csproj /p:SimHubInstallPath=C:\does-not-exist
```

Expected: PASS, with the known LiteDB `NU1904` warning acceptable if it appears.

- [ ] **Step 5: Commit Task 4**

```powershell
git add StatsPlus\SettingsControl.xaml StatsPlus.Tests\SettingsControlXamlTests.cs
git commit -m "Show circuit columns in StatsPlus history"
```

---

### Task 5: Full Verification And Future Filter Notes

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: completed Tasks 1-4.
- Produces: a short README note that profiles own game-specific display behavior and that display fields are fuzzy search targets while raw identity remains unchanged.

- [ ] **Step 1: Add documentation note**

Add this note to the README near the existing stored history fields:

```markdown
StatsPlus resolves supported games through lightweight game profiles. Profiles own recording-toggle lookup, debug settings keys, track display mapping, circuit/layout display, and small game-specific sector-layout rules.

Stored-history UI rows resolve the raw `TrackNameWithConfig` into display-oriented fields before binding:

- `TrackNameWithConfigDisplay` keeps the full resolved track display text.
- `CircuitNameDisplay` and `CircuitLayoutDisplay` mirror Affinity's track display columns.
- `CarModelDisplay` keeps a display-friendly car label when available.

Raw `GameName`, `CarModel`, and `TrackNameWithConfig` remain the lookup identity for lap rows and personal-best properties. Display fields are intentionally fuzzy search targets. An Affinity-style track search should match `TrackNameWithConfigDisplay`, `CircuitNameDisplay`, and `CircuitLayoutDisplay` together. Future exact filters should query raw `GameName`, `CarModel`, `RawTrackName`, and `TrackNameWithConfig`; if exact layout filtering is needed, add a separate transient raw layout key then instead of treating `CircuitLayoutDisplay` as a durable identity.
```

- [ ] **Step 2: Run all tests**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj
```

Expected: PASS.

- [ ] **Step 3: Run no-deploy plugin build**

Run:

```powershell
dotnet build StatsPlus\StatsPlus.csproj /p:SimHubInstallPath=C:\does-not-exist
```

Expected: PASS, with the known LiteDB `NU1904` warning acceptable if it appears.

- [ ] **Step 4: Inspect diff**

Run:

```powershell
git diff --check
git diff --stat
git diff
```

Expected: no whitespace errors; changes are limited to the profile registry, view models, plugin profile routing/display population, XAML columns, tests, and README note.

- [ ] **Step 5: Commit Task 5**

```powershell
git add README.md
git commit -m "Document StatsPlus game profiles and circuit display fields"
```

---

## Self-Review

- Spec coverage: The plan introduces the approved lightweight game-profile registry, routes the current display-related game checks through profiles, mirrors Affinity's circuit/layout display rules, leaves storage identity unchanged, updates the StatsPlus grid, and preserves future display-search targets.
- Gap scan: Future filter UI is explicitly out of scope for this display change; exact future filters are documented as raw-key work, not display-field identity work.
- Scope control: The plan avoids a full plugin rewrite by moving only recording/debug key, track display, circuit display, and sector-layout behavior into profiles now.
- Type consistency: `IStatsPlusGameProfile`, `StatsPlusGameProfileRegistry.Resolve`, `CircuitDisplayParts`, `CircuitNameDisplay`, and `CircuitLayoutDisplay` are named consistently across tasks.
- Testing coverage: Profile tests cover game aliases, settings keys, recording toggles, display mapping, split rules, and sector inference. Plugin tab tests cover population, display-collision raw selected-row lookup, and personal-best raw-key preservation. A static XAML test covers the visible column switch. Full project test/build commands verify integration.
