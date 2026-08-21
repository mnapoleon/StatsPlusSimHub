# StatsPlus Game-Profile Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the remaining game-name classification from `StatsPlusPlugin` so supported-game metadata and the Assetto-family lap-boundary exception are owned by `IStatsPlusGameProfile` implementations.

**Architecture:** Keep `StatsPlusPlugin` responsible for game-agnostic telemetry orchestration, settings persistence, logging, and storage. Extend the existing profile contract with one narrowly named lap-boundary capability, share that capability through an Assetto-family profile base class, and use `StatsPlusGameProfileRegistry.SupportedProfiles` as the supported-game catalog for diagnostic settings and options.

**Tech Stack:** C# (`net48`, latest language version), WPF, MSTest 3.6.2, SimHub SDK stubs, LiteDB 4.1.4

**Spec:** Follow-up to `docs/superpowers/specs/2026-08-21-statsplus-circuit-layout-display-design.md` (Game Profile Architecture and deferred full migration), governed by `.codex/project-practice.md` items 12-14.

## Global Constraints

- Start execution from a clean feature branch or worktree; do not implement directly on `main`.
- Put game-specific differences behind `IStatsPlusGameProfile`; do not add a replacement `IsXGame` helper or compare normalized game names in `StatsPlusPlugin`.
- Preserve existing recording toggles, JSON property names, XAML bindings, LiteDB documents/indexes, personal-best property names, and raw game/car/track identity.
- Preserve current lap-capture behavior: Assetto Corsa, Assetto Corsa Competizione, and Assetto Corsa EVO may use captured sector data as evidence that a same-time lap is fresh; other and unknown games may not.
- Keep diagnostic logging centralized in `StatsPlusPlugin`, but derive supported settings keys and labels from `StatsPlusGameProfileRegistry.SupportedProfiles`.
- Do not deploy to SimHub during automated verification. Build with `/p:SimHubInstallPath=C:\does-not-exist`.
- The LiteDB 4.1.4 `NU1904` warning is known and accepted; do not change dependency versions in this work.
- Include this plan file and the README update in the implementation branch's commits.

---

## File Structure

- Modify `StatsPlus/StatsPlusGameProfiles.cs`: add the lap-boundary capability to the profile contract, provide the default behavior, and centralize Assetto-family behavior in a shared profile base.
- Modify `StatsPlus/StatsPlusPlugin.cs`: consume the profile capability when queueing a lap, remove `IsAssettoCorsaGame`, and derive diagnostic defaults/options from `SupportedProfiles`.
- Modify `StatsPlus.Tests/StatsPlusGameProfileTests.cs`: directly verify which profiles expose the lap-boundary capability and guard supported-profile metadata.
- Modify `StatsPlus.Tests/StatsPlusPluginLapCaptureTests.cs`: retain end-to-end proof that same-time Assetto-family laps with changing sectors are recorded.
- Modify `StatsPlus.Tests/StatsPlusPluginLmuSupportTests.cs`: remove the reflection test that requires the obsolete plugin-level family classifier.
- Modify `StatsPlus.Tests/StatsPlusDebugLoggingSettingsTests.cs`: prove diagnostic options exactly match supported profiles.
- Modify `README.md`: document the completed profile boundary and the intentionally game-agnostic plugin orchestration.

### Task 1: Move Assetto-family lap-boundary evidence into profiles

**Files:**
- Modify: `StatsPlus/StatsPlusGameProfiles.cs:24-32,68-221`
- Modify: `StatsPlus/StatsPlusPlugin.cs:1069-1094,1361-1373`
- Modify: `StatsPlus.Tests/StatsPlusGameProfileTests.cs:97-110`
- Modify: `StatsPlus.Tests/StatsPlusPluginLapCaptureTests.cs:187-217`
- Modify: `StatsPlus.Tests/StatsPlusPluginLmuSupportTests.cs:59-68`

**Interfaces:**
- Consumes: `StatsPlusGameProfileRegistry.Resolve(string gameName)` and the plugin's `_capturedSector1` / `_capturedSector2` flags.
- Produces: `IStatsPlusGameProfile.UsesCapturedSectorsAsLapBoundaryEvidence : bool`, defaulting to `false` and returning `true` for the three Assetto-family profiles.

- [ ] **Step 1: Add the failing profile capability test**

Append this test to `StatsPlus.Tests/StatsPlusGameProfileTests.cs`:

```csharp
[TestMethod]
public void LapBoundaryEvidence_UsesCapturedSectorsOnlyForAssettoFamily()
{
    StatsPlusGameProfileRegistry registry = StatsPlusGameProfileRegistry.CreateDefault();

    Assert.IsTrue(registry.Resolve("AssettoCorsa").UsesCapturedSectorsAsLapBoundaryEvidence);
    Assert.IsTrue(registry.Resolve("Assetto Corsa Competizione").UsesCapturedSectorsAsLapBoundaryEvidence);
    Assert.IsTrue(registry.Resolve("Assetto Corsa EVO").UsesCapturedSectorsAsLapBoundaryEvidence);
    Assert.IsFalse(registry.Resolve("Automobilista2").UsesCapturedSectorsAsLapBoundaryEvidence);
    Assert.IsFalse(registry.Resolve("LMU").UsesCapturedSectorsAsLapBoundaryEvidence);
    Assert.IsFalse(registry.Resolve("UnknownGame").UsesCapturedSectorsAsLapBoundaryEvidence);
}
```

- [ ] **Step 2: Run the focused test and confirm it fails to compile**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter FullyQualifiedName~StatsPlusGameProfileTests
```

Expected: FAIL with `IStatsPlusGameProfile` missing `UsesCapturedSectorsAsLapBoundaryEvidence`.

- [ ] **Step 3: Add the profile capability and Assetto-family base class**

Add the property to `IStatsPlusGameProfile` immediately after `IsRecordingEnabled`:

```csharp
bool UsesCapturedSectorsAsLapBoundaryEvidence { get; }
```

Add the safe default to `StatsPlusGameProfileBase`:

```csharp
public virtual bool UsesCapturedSectorsAsLapBoundaryEvidence => false;
```

Add this focused base class before `AssettoCorsaProfile`:

```csharp
internal abstract class AssettoFamilyStatsPlusGameProfileBase : StatsPlusGameProfileBase
{
    protected AssettoFamilyStatsPlusGameProfileBase(
        string settingsKey,
        string displayName,
        params string[] aliases)
        : base(settingsKey, displayName, aliases)
    {
    }

    public override bool UsesCapturedSectorsAsLapBoundaryEvidence => true;

    public override void InferSectorLayout(
        double lapTime,
        ref double sector1,
        ref double sector2,
        ref double sector3)
    {
        InferAssettoFamilySectorLayout(lapTime, ref sector1, ref sector2, ref sector3);
    }
}
```

Change `AssettoCorsaProfile`, `AssettoCorsaCompetizioneProfile`, and `AssettoCorsaEvoProfile` to inherit from `AssettoFamilyStatsPlusGameProfileBase`. Remove their now-redundant `InferSectorLayout` overrides; keep each recording-toggle, track-display, and circuit-display override unchanged.

- [ ] **Step 4: Run the profile tests and confirm they pass**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter FullyQualifiedName~StatsPlusGameProfileTests
```

Expected: PASS, including the new Assetto-family capability test and the existing sector-layout test.

- [ ] **Step 5: Broaden the lap-capture regression test before changing plugin routing**

Convert `DataUpdate_RecordsAssettoCorsaBackToBackLapsWithSameTimeWhenSectorsChange` in `StatsPlus.Tests/StatsPlusPluginLapCaptureTests.cs` into this data-driven signature:

```csharp
[DataTestMethod]
[DataRow("AssettoCorsa")]
[DataRow("AssettoCorsaCompetizione")]
[DataRow("AssettoCorsaEvo")]
public void DataUpdate_RecordsAssettoFamilyBackToBackLapsWithSameTimeWhenSectorsChange(string gameName)
```

In that test only, replace every `gameName: "AssettoCorsa"` argument with `gameName: gameName`. Leave the timing and sector sequence unchanged so the test continues to prove that two equal `48.815` laps with different captured sectors are stored.

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter FullyQualifiedName~DataUpdate_RecordsAssettoFamilyBackToBackLapsWithSameTimeWhenSectorsChange
```

Expected: PASS for all three data rows against the current behavior.

- [ ] **Step 6: Route lap-boundary evidence through the resolved profile**

Replace the direct family check in `QueueLapCaptureIfNeeded` with:

```csharp
IStatsPlusGameProfile profile = _gameProfiles.Resolve(data.GameName);
bool hasProfileSectorEvidence =
    profile.UsesCapturedSectorsAsLapBoundaryEvidence &&
    (_capturedSector1 || _capturedSector2);

_pendingLastLapTimeNeedsRefresh = _pendingObservedLastLapSeconds <= 0 ||
    (AreClose(_pendingObservedLastLapSeconds, previousLastLapSeconds) &&
     LastLapSeconds > 0 &&
     AreClose(_pendingObservedLastLapSeconds, LastLapSeconds) &&
     !hasProfileSectorEvidence);
```

Delete `StatsPlusPlugin.IsAssettoCorsaGame`. Delete `IsAssettoCorsaGame_TreatsCompetizioneAsAssettoFamily` from `StatsPlus.Tests/StatsPlusPluginLmuSupportTests`; the new profile test supersedes this reflection test.

- [ ] **Step 7: Run the profile and lap-capture regression suites**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter "FullyQualifiedName~StatsPlusGameProfileTests|FullyQualifiedName~StatsPlusPluginLapCaptureTests|FullyQualifiedName~StatsPlusPluginLmuSupportTests"
```

Expected: PASS. The three Assetto-family rows record both same-time laps, and LMU delayed-last-lap tests remain unchanged.

- [ ] **Step 8: Commit the lap-boundary profile migration**

```powershell
git add -- StatsPlus/StatsPlusGameProfiles.cs StatsPlus/StatsPlusPlugin.cs StatsPlus.Tests/StatsPlusGameProfileTests.cs StatsPlus.Tests/StatsPlusPluginLapCaptureTests.cs StatsPlus.Tests/StatsPlusPluginLmuSupportTests.cs
git commit -m "refactor: move lap evidence into game profiles"
```

### Task 2: Make supported profiles the diagnostic-game catalog

**Files:**
- Modify: `StatsPlus/StatsPlusPlugin.cs:29-39,1398-1467,1507-1513`
- Modify: `StatsPlus.Tests/StatsPlusGameProfileTests.cs:10-24`
- Modify: `StatsPlus.Tests/StatsPlusDebugLoggingSettingsTests.cs:58-88`

**Interfaces:**
- Consumes: `StatsPlusGameProfileRegistry.SupportedProfiles : IReadOnlyList<IStatsPlusGameProfile>` and each profile's canonical `SettingsKey` and `DisplayName`.
- Produces: the same eight default diagnostic settings and sorted WPF options without a second hard-coded game list in `StatsPlusPlugin`.

- [ ] **Step 1: Guard the supported-profile catalog metadata**

Add `using System.Linq;` to `StatsPlus.Tests/StatsPlusGameProfileTests.cs`, then append:

```csharp
[TestMethod]
public void SupportedProfiles_HaveUniqueCanonicalSettingsKeysAndDisplayNames()
{
    StatsPlusGameProfileRegistry registry = StatsPlusGameProfileRegistry.CreateDefault();
    IStatsPlusGameProfile[] profiles = registry.SupportedProfiles.ToArray();

    Assert.AreEqual(8, profiles.Length);
    Assert.IsTrue(profiles.All(profile => !string.IsNullOrWhiteSpace(profile.SettingsKey)));
    Assert.IsTrue(profiles.All(profile => !string.IsNullOrWhiteSpace(profile.DisplayName)));
    Assert.AreEqual(
        profiles.Length,
        profiles.Select(profile => profile.SettingsKey).Distinct().Count());
}
```

This is a catalog invariant test, so it should pass before the refactor.

- [ ] **Step 2: Characterize diagnostic options against the profile registry**

Append this test to `StatsPlus.Tests/StatsPlusDebugLoggingSettingsTests.cs`:

```csharp
[TestMethod]
public void RefreshGameDebugLoggingOptions_MatchesSupportedProfiles()
{
    StatsPlusPlugin plugin = new StatsPlusPlugin();
    StatsPlusGameProfileRegistry registry = StatsPlusGameProfileRegistry.CreateDefault();
    Invoke(plugin, "EnsureDefaultGameDebugLoggingSettings");
    Invoke(plugin, "RefreshGameDebugLoggingOptions");

    string[] expectedKeys = registry.SupportedProfiles
        .OrderBy(profile => profile.DisplayName)
        .Select(profile => profile.SettingsKey)
        .ToArray();
    string[] actualKeys = plugin.GameDebugLoggingOptions
        .Select(option => option.SettingsKey)
        .ToArray();

    CollectionAssert.AreEqual(expectedKeys, actualKeys);
}
```

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter "FullyQualifiedName~StatsPlusGameProfileTests|FullyQualifiedName~StatsPlusDebugLoggingSettingsTests"
```

Expected: PASS, establishing a behavior-preserving baseline before removing the duplicate list.

- [ ] **Step 3: Remove `DefaultGameDebugLoggingEntries` and seed settings from profiles**

Delete the `DefaultGameDebugLoggingEntries` field from `StatsPlusPlugin`. In `EnsureDefaultGameDebugLoggingSettings`, replace its loop with:

```csharp
foreach (IStatsPlusGameProfile profile in _gameProfiles.SupportedProfiles)
{
    if (!Settings.GameDebugLogging.ContainsKey(profile.SettingsKey))
    {
        Settings.GameDebugLogging[profile.SettingsKey] = false;
    }
}
```

Keep `RemoveUnsupportedGameDebugLoggingSettings()` before this loop so stale or alias keys are still removed before canonical defaults are added.

- [ ] **Step 4: Build diagnostic options directly from supported profiles**

Replace `RefreshGameDebugLoggingOptions` with:

```csharp
private void RefreshGameDebugLoggingOptions()
{
    GameDebugLoggingOptions.Clear();
    foreach (IStatsPlusGameProfile profile in _gameProfiles.SupportedProfiles.OrderBy(item => item.DisplayName))
    {
        bool isEnabled = Settings.GameDebugLogging != null &&
            Settings.GameDebugLogging.TryGetValue(profile.SettingsKey, out bool configuredEnabled) &&
            configuredEnabled;
        GameDebugLoggingOptions.Add(new GameDebugLoggingOption(
            profile.SettingsKey,
            profile.DisplayName,
            isEnabled,
            UpdateGameDebugLoggingSetting));
    }
}
```

Delete `GetDebugLoggingDisplayName`; after this change it has no production callers. Keep `GetDebugLoggingSettingsKey` and `IsSupportedDebugLoggingSettingsKey`, because runtime aliases and persisted-key cleanup still need registry resolution.

- [ ] **Step 5: Run diagnostic and profile tests**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter "FullyQualifiedName~StatsPlusGameProfileTests|FullyQualifiedName~StatsPlusDebugLoggingSettingsTests|FullyQualifiedName~StatsPlusDiagnosticLoggingTests"
```

Expected: PASS. The settings dictionary contains exactly the supported canonical keys, options use profile display names in sorted order, RaceRoom aliases still resolve to `raceroomracingexperience`, and per-game log paths are unchanged.

- [ ] **Step 6: Commit the supported-game catalog migration**

```powershell
git add -- StatsPlus/StatsPlusPlugin.cs StatsPlus.Tests/StatsPlusGameProfileTests.cs StatsPlus.Tests/StatsPlusDebugLoggingSettingsTests.cs
git commit -m "refactor: derive supported games from profiles"
```

### Task 3: Document and verify the completed boundary

**Files:**
- Modify: `README.md:46`
- Include: `docs/superpowers/plans/2026-08-21-statsplus-game-profile-completion.md`

**Interfaces:**
- Consumes: the completed `IStatsPlusGameProfile` contract and `StatsPlusGameProfileRegistry.SupportedProfiles` catalog.
- Produces: a documented invariant that future game-specific runtime behavior is added to profiles rather than direct game-name branches in `StatsPlusPlugin`.

- [ ] **Step 1: Update the profile architecture note**

Replace the existing one-sentence profile paragraph in `README.md` with:

```markdown
StatsPlus resolves supported games through lightweight game profiles. Profiles own recording-toggle lookup, supported-game/debug metadata, track display mapping, circuit/layout display, sector-layout inference, and game-specific lap-boundary capabilities. `StatsPlusPlugin` owns game-agnostic telemetry orchestration and resolves a profile instead of classifying games directly.
```

- [ ] **Step 2: Audit production code for leftover direct game classification**

Run:

```powershell
rg -n "IsAssettoCorsaGame|DefaultGameDebugLoggingEntries|StatsPlusGameName\.Normalize" StatsPlus/StatsPlusPlugin.cs
```

Expected: no matches.

Run:

```powershell
rg -n -i "assetto|competizione|automobilista|iracing|le mans|lmu|rfactor|raceroom|r3e|rrre" StatsPlus/StatsPlusPlugin.cs
```

Expected: remaining matches are only Assetto Corsa track-map resource plumbing (`_assettoCorsaTrackMap`, `_acTrackMapPath`, `LoadAssettoCorsaTrackMap`, and `StatsPlusTrackDisplayContext`). There must be no game-name comparison, switch, family classifier, or hard-coded supported-game catalog in the plugin.

- [ ] **Step 3: Run the complete automated test suite**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj
```

Expected: PASS. `NU1904` for LiteDB 4.1.4 may appear and is accepted.

- [ ] **Step 4: Build the plugin without deploying to SimHub**

Run:

```powershell
dotnet build StatsPlus\StatsPlus.csproj /p:SimHubInstallPath=C:\does-not-exist
```

Expected: build succeeds; no files are copied to `C:\Program Files (x86)\SimHub`; the known LiteDB warning may remain.

- [ ] **Step 5: Inspect the final diff and whitespace**

Run:

```powershell
git diff --check
git status --short
git diff -- StatsPlus/StatsPlusGameProfiles.cs StatsPlus/StatsPlusPlugin.cs StatsPlus.Tests/StatsPlusGameProfileTests.cs StatsPlus.Tests/StatsPlusPluginLapCaptureTests.cs StatsPlus.Tests/StatsPlusPluginLmuSupportTests.cs StatsPlus.Tests/StatsPlusDebugLoggingSettingsTests.cs README.md docs/superpowers/plans/2026-08-21-statsplus-game-profile-completion.md
```

Expected: no whitespace errors; the diff is limited to the profile contract/implementations, plugin routing/catalog cleanup, focused tests, README, and this plan.

- [ ] **Step 6: Commit documentation and the implementation plan**

```powershell
git add -- README.md docs/superpowers/plans/2026-08-21-statsplus-game-profile-completion.md
git commit -m "docs: define StatsPlus game profile ownership"
```

## Self-Review Results

- **Spec coverage:** The plan completes the profile migration explicitly deferred by the circuit-layout design, covers the only remaining runtime game-family branch, and removes the plugin's duplicate supported-game/debug catalog.
- **Scope control:** `PluginSettings` properties, recording checkbox XAML, Assetto track-map resource loading, storage identities, and generic lap-state orchestration remain unchanged because none directly classify the current game in plugin runtime logic.
- **Type consistency:** Every task uses `IStatsPlusGameProfile.UsesCapturedSectorsAsLapBoundaryEvidence : bool`, `StatsPlusGameProfileRegistry.Resolve(string)`, and `SupportedProfiles : IReadOnlyList<IStatsPlusGameProfile>` consistently.
- **Regression coverage:** Profile tests own capability/catalog rules; plugin integration tests retain same-time Assetto-family lap capture; diagnostic tests retain settings, labels, aliases, and log-path behavior; the full suite and no-deploy build close verification.
