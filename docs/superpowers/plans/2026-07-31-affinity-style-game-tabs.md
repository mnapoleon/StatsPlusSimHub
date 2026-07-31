# Affinity-Style Game Tabs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign the StatsPlus SimHub settings UI into dynamic per-game tabs plus a final Settings tab, with an Affinity-style fixed live status bar above the scrollable content.

**Architecture:** Keep the existing StatsPlus plugin object as the view model, matching current project style. Extend the existing `GameHistoryTab` model, add a `StatsPlusSettingsTab` marker model, expose a bound `TopLevelTabs` collection, and rewrite `SettingsControl.xaml` to use data templates for game tabs and the settings tab. Add live telemetry state properties directly on `StatsPlusPlugin`, mirroring Affinity's `IsTelemetryActive`, `LiveStatusLabel`, and `StatusSectionForeground` pattern.

**Tech Stack:** C# net48, WPF XAML, SimHub `SHTabControl`/`SHSection`, MSTest, LiteDB 4.1.4, existing SimHub SDK stubs.

## Global Constraints

- Never commit to `main`; all work stays on a feature branch such as `codex/affinity-style-game-tabs-spec`.
- The top-level StatsPlus UI has no Overview tab.
- The top-level StatsPlus UI shows one tab per recorded game and a final Settings tab.
- When there is no recorded game history yet, the tab control shows only `Settings`.
- The live status bar is outside and above the `ScrollViewer`.
- The live status label and `DataStatus` are green while StatsPlus is actively recording telemetry and red while StatsPlus is standby, disabled, unsupported, or waiting for usable telemetry.
- Do not redesign telemetry capture or lap persistence.
- Do not add game logos.
- Keep existing history actions: refresh history, clear selected game, clear all data, and toggle selected lap validity.
- Keep existing code-behind handlers in `SettingsControl.xaml.cs`.

---

## File Structure

- Modify `StatsPlus/LapHistoryModels.cs`
  - Add `GameHistoryTab.Header`.
  - Add `StatsPlusSettingsTab`.
- Modify `StatsPlus/StatsPlusPlugin.cs`
  - Add top-level tab collection and selected tab state.
  - Preserve dynamic tab selection across history refreshes.
  - Add live status color/state properties.
  - Update data/update and clear paths to keep live status state correct.
- Replace layout in `StatsPlus/SettingsControl.xaml`
  - Add outer fixed live status bar row.
  - Move tab control into row 1 `ScrollViewer`.
  - Replace fixed `SHTabItem`s with data-template-driven `SHTabControl`.
  - Move existing settings sections into `StatsPlusSettingsTab` template.
  - Move existing lap grids/actions into `GameHistoryTab` template.
- Keep `StatsPlus/SettingsControl.xaml.cs`
  - No new handlers required; existing handlers call plugin methods.
- Create `StatsPlus.Tests/StatsPlusPluginTabLayoutTests.cs`
  - Covers dynamic tabs, tab selection preservation, selected-track loading, and clear-game selection behavior.
- Create `StatsPlus.Tests/StatsPlusPluginLiveStatusTests.cs`
  - Covers standby/recording label and color state.

---

### Task 1: Tab Model And Initial Top-Level Tab Tests

**Files:**
- Modify: `StatsPlus/LapHistoryModels.cs`
- Modify: `StatsPlus/StatsPlusPlugin.cs`
- Create: `StatsPlus.Tests/StatsPlusPluginTabLayoutTests.cs`

**Interfaces:**
- Consumes: existing `GameHistoryTab.GameName`, `GameHistoryTab.Tracks`, `StatsPlusPlugin.RefreshStoredTrackSummaries()`.
- Produces:
  - `GameHistoryTab.Header : string`
  - `StatsPlusSettingsTab.Header : string`
  - `StatsPlusPlugin.TopLevelTabs : ObservableCollection<object>`
  - `StatsPlusPlugin.SelectedTopLevelTab : object`

- [ ] **Step 1: Write failing tests for the settings-only initial tab state**

Create `StatsPlus.Tests/StatsPlusPluginTabLayoutTests.cs` with this content:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimHub.Plugins;

namespace StatsPlus.Tests
{
    [TestClass]
    public class StatsPlusPluginTabLayoutTests
    {
        private string _tempDirectory;
        private StatsPlusPlugin _plugin;
        private TestPluginManager _pluginManager;

        [TestInitialize]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "StatsPlusPluginTabLayoutTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        [TestCleanup]
        public void TearDown()
        {
            _plugin?.End(_pluginManager);
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        [TestMethod]
        public void Init_WithNoHistory_CreatesSettingsOnlyTopLevelTabs()
        {
            StatsPlusPlugin plugin = CreateInitializedPlugin();

            Assert.AreEqual(1, plugin.TopLevelTabs.Count);
            Assert.IsInstanceOfType(plugin.TopLevelTabs[0], typeof(StatsPlusSettingsTab));
            Assert.AreEqual("Settings", ((StatsPlusSettingsTab)plugin.TopLevelTabs[0]).Header);
            Assert.AreSame(plugin.TopLevelTabs[0], plugin.SelectedTopLevelTab);
            Assert.IsNull(plugin.SelectedGameHistoryTab);
            Assert.IsFalse(plugin.HasSelectedGameHistoryTab);
        }

        private StatsPlusPlugin CreateInitializedPlugin()
        {
            _plugin = new StatsPlusPlugin();
            _pluginManager = new TestPluginManager(_tempDirectory);
            _plugin.Init(_pluginManager);
            return _plugin;
        }

        private sealed class TestPluginManager : PluginManager
        {
            private readonly string _commonStorageRoot;

            public TestPluginManager(string tempDirectory)
            {
                _commonStorageRoot = Path.Combine(tempDirectory, "PluginsData", "Common");
            }

            public Dictionary<string, object> Properties { get; } = new Dictionary<string, object>();

            public override string GetCommonStoragePath(params string[] pathParts)
            {
                Directory.CreateDirectory(_commonStorageRoot);
                return Path.Combine(new[] { _commonStorageRoot }.Concat(pathParts ?? Array.Empty<string>()).ToArray());
            }

            public override string GetCommonStoragePath(bool create, params string[] pathParts)
            {
                return GetCommonStoragePath(pathParts);
            }

            public override void AddProperty<T>(string propertyName, Type ownerType, T initialValue, string unit = null)
            {
                Properties[propertyName] = initialValue;
            }

            public override void SetPropertyValue(string propertyName, Type ownerType, object value)
            {
                Properties[propertyName] = value;
            }
        }
    }
}
```

- [ ] **Step 2: Run the new test and verify it fails**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter "FullyQualifiedName~StatsPlusPluginTabLayoutTests.Init_WithNoHistory_CreatesSettingsOnlyTopLevelTabs"
```

Expected: FAIL because `StatsPlusPlugin.TopLevelTabs`, `StatsPlusPlugin.SelectedTopLevelTab`, and `StatsPlusSettingsTab` do not exist.

- [ ] **Step 3: Add model and plugin properties**

In `StatsPlus/LapHistoryModels.cs`, add `Header` to `GameHistoryTab` and add `StatsPlusSettingsTab`:

```csharp
public class GameHistoryTab
{
    public string Header => GameName;

    public string GameName { get; set; } = string.Empty;

    public List<StoredTrackSummary> Tracks { get; set; } = new List<StoredTrackSummary>();

    public override string ToString()
    {
        return GameName;
    }
}

public class StatsPlusSettingsTab
{
    public string Header => "Settings";
}
```

In `StatsPlus/StatsPlusPlugin.cs`, add fields near the existing selected-tab fields:

```csharp
private readonly StatsPlusSettingsTab _settingsTab = new StatsPlusSettingsTab();
private object _selectedTopLevelTab;
```

Add public properties near `GameHistoryTabs`:

```csharp
public ObservableCollection<object> TopLevelTabs { get; } = new ObservableCollection<object>();

public object SelectedTopLevelTab
{
    get => _selectedTopLevelTab;
    set
    {
        if (ReferenceEquals(_selectedTopLevelTab, value))
        {
            return;
        }

        _selectedTopLevelTab = value;
        OnPropertyChanged();

        if (value is GameHistoryTab gameTab)
        {
            SelectedGameHistoryTab = gameTab;
        }
        else
        {
            SelectedGameHistoryTab = null;
        }
    }
}
```

In `RefreshStoredTrackSummaries()`, inside the local `Apply()` method after repopulating `GameHistoryTabs`, also repopulate `TopLevelTabs` and default selection:

```csharp
TopLevelTabs.Clear();
foreach (GameHistoryTab tab in tabs)
{
    TopLevelTabs.Add(tab);
}

TopLevelTabs.Add(_settingsTab);

if (SelectedTopLevelTab == null || !TopLevelTabs.Contains(SelectedTopLevelTab))
{
    SelectedTopLevelTab = TopLevelTabs.OfType<GameHistoryTab>().FirstOrDefault() ?? (object)_settingsTab;
}
```

- [ ] **Step 4: Run the new test and verify it passes**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter "FullyQualifiedName~StatsPlusPluginTabLayoutTests.Init_WithNoHistory_CreatesSettingsOnlyTopLevelTabs"
```

Expected: PASS.

- [ ] **Step 5: Commit Task 1**

Run:

```powershell
git status --short --branch
git add -- StatsPlus/LapHistoryModels.cs StatsPlus/StatsPlusPlugin.cs StatsPlus.Tests/StatsPlusPluginTabLayoutTests.cs
git commit -m "Add StatsPlus top-level tab models"
```

Expected: commit created on the current feature branch, not on `main`.

---

### Task 2: Dynamic Game Tabs And Selection Preservation

**Files:**
- Modify: `StatsPlus/StatsPlusPlugin.cs`
- Modify: `StatsPlus.Tests/StatsPlusPluginTabLayoutTests.cs`

**Interfaces:**
- Consumes:
  - `StatsPlusPlugin.TopLevelTabs`
  - `StatsPlusPlugin.SelectedTopLevelTab`
  - `StatsPlusPlugin.SelectedGameHistoryTab`
  - `StatsPlusLiteDbRepository.AddLap(...)`
- Produces:
  - `StatsPlusPlugin.ResolveSelectedTopLevelTab(object previousSelectedTopLevelTab, IReadOnlyList<GameHistoryTab> refreshedTabs, StatsPlusSettingsTab settingsTab) : object`
  - refresh behavior that preserves selection by game name and falls back to Settings when no game remains.

- [ ] **Step 1: Add failing tests for game tab order, selection preservation, selected-track loading, and clear behavior**

Append these tests and helper methods inside `StatsPlusPluginTabLayoutTests`:

```csharp
[TestMethod]
public void Init_WithHistory_CreatesGameTabsFollowedBySettings()
{
    SeedLap("iRacing", "Mazda MX-5", "Laguna Seca", "Laguna Seca", 91.25, new DateTime(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc));
    SeedLap("LMU", "Ferrari 499P", "Le Mans", "Le Mans - 24h", 210.5, new DateTime(2026, 7, 31, 13, 0, 0, DateTimeKind.Utc));

    StatsPlusPlugin plugin = CreateInitializedPlugin();

    CollectionAssert.AreEqual(
        new[] { "iRacing", "LMU", "Settings" },
        TabHeaders(plugin));
    Assert.IsInstanceOfType(plugin.SelectedTopLevelTab, typeof(GameHistoryTab));
    Assert.AreEqual("iRacing", ((GameHistoryTab)plugin.SelectedTopLevelTab).GameName);
    Assert.AreSame(plugin.SelectedTopLevelTab, plugin.SelectedGameHistoryTab);
}

[TestMethod]
public void RefreshStoredTrackSummaries_PreservesSelectedGameTabByName()
{
    SeedLap("iRacing", "Mazda MX-5", "Laguna Seca", "Laguna Seca", 91.25, new DateTime(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc));
    SeedLap("LMU", "Ferrari 499P", "Le Mans", "Le Mans - 24h", 210.5, new DateTime(2026, 7, 31, 13, 0, 0, DateTimeKind.Utc));
    StatsPlusPlugin plugin = CreateInitializedPlugin();
    GameHistoryTab lmuTab = plugin.TopLevelTabs.OfType<GameHistoryTab>().Single(tab => tab.GameName == "LMU");
    plugin.SelectedTopLevelTab = lmuTab;

    plugin.RefreshStoredTrackSummaries();

    Assert.IsInstanceOfType(plugin.SelectedTopLevelTab, typeof(GameHistoryTab));
    Assert.AreEqual("LMU", ((GameHistoryTab)plugin.SelectedTopLevelTab).GameName);
    Assert.AreSame(plugin.SelectedTopLevelTab, plugin.SelectedGameHistoryTab);
}

[TestMethod]
public void SelectedTrackSummary_LoadsRecordedLapsForSelectedCombo()
{
    SeedLap("LMU", "Ferrari 499P", "Le Mans", "Le Mans - 24h", 210.5, new DateTime(2026, 7, 31, 13, 0, 0, DateTimeKind.Utc));
    SeedLap("LMU", "Ferrari 499P", "Le Mans", "Le Mans - 24h", 208.4, new DateTime(2026, 7, 31, 13, 5, 0, DateTimeKind.Utc));
    StatsPlusPlugin plugin = CreateInitializedPlugin();
    GameHistoryTab lmuTab = plugin.TopLevelTabs.OfType<GameHistoryTab>().Single(tab => tab.GameName == "LMU");

    plugin.SelectedTrackSummary = lmuTab.Tracks.Single();

    Assert.AreEqual(2, plugin.SelectedTrackLaps.Count);
    Assert.AreEqual(208.4, plugin.SelectedTrackLaps[0].LapTimeSeconds, 0.0001);
    Assert.AreEqual(210.5, plugin.SelectedTrackLaps[1].LapTimeSeconds, 0.0001);
}

[TestMethod]
public void ClearSelectedGameData_RemovesGameTabAndSelectsRemainingGameOrSettings()
{
    SeedLap("iRacing", "Mazda MX-5", "Laguna Seca", "Laguna Seca", 91.25, new DateTime(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc));
    SeedLap("LMU", "Ferrari 499P", "Le Mans", "Le Mans - 24h", 210.5, new DateTime(2026, 7, 31, 13, 0, 0, DateTimeKind.Utc));
    StatsPlusPlugin plugin = CreateInitializedPlugin();
    plugin.SelectedTopLevelTab = plugin.TopLevelTabs.OfType<GameHistoryTab>().Single(tab => tab.GameName == "LMU");

    plugin.ClearSelectedGameData();

    CollectionAssert.AreEqual(
        new[] { "iRacing", "Settings" },
        TabHeaders(plugin));
    Assert.AreEqual("iRacing", ((GameHistoryTab)plugin.SelectedTopLevelTab).GameName);

    plugin.ClearSelectedGameData();

    CollectionAssert.AreEqual(
        new[] { "Settings" },
        TabHeaders(plugin));
    Assert.IsInstanceOfType(plugin.SelectedTopLevelTab, typeof(StatsPlusSettingsTab));
    Assert.IsNull(plugin.SelectedGameHistoryTab);
}

private void SeedLap(string gameName, string carModel, string trackName, string trackNameWithConfig, double lapSeconds, DateTime timestampUtc)
{
    string databasePath = Path.Combine(_tempDirectory, "PluginsData", "StatsPlus", "StatsPlus.laps.ldb");
    using (var repository = new StatsPlusLiteDbRepository(databasePath))
    {
        repository.Initialize();
        repository.AddLap(gameName, carModel, trackName, trackNameWithConfig, new RecordedLap
        {
            LapNumber = 1,
            LapTimeSeconds = lapSeconds,
            Sector1Seconds = 0.0,
            Sector2Seconds = 0.0,
            Sector3Seconds = lapSeconds,
            IsValid = true,
            TimestampUtc = timestampUtc
        });
    }
}

private static string[] TabHeaders(StatsPlusPlugin plugin)
{
    return plugin.TopLevelTabs
        .Select(tab =>
        {
            if (tab is GameHistoryTab gameTab)
            {
                return gameTab.Header;
            }

            return ((StatsPlusSettingsTab)tab).Header;
        })
        .ToArray();
}
```

- [ ] **Step 2: Run the tab layout tests and verify failures**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter "FullyQualifiedName~StatsPlusPluginTabLayoutTests"
```

Expected: at least the selection-preservation and clear-game tests fail because `RefreshStoredTrackSummaries()` only preserves object identity from the just-cleared collection and does not resolve selection by game name.

- [ ] **Step 3: Implement deterministic tab selection resolution**

In `StatsPlus/StatsPlusPlugin.cs`, add this helper near the other internal static helpers:

```csharp
internal static object ResolveSelectedTopLevelTab(
    object previousSelectedTopLevelTab,
    IReadOnlyList<GameHistoryTab> refreshedTabs,
    StatsPlusSettingsTab settingsTab)
{
    if (settingsTab == null)
    {
        throw new ArgumentNullException(nameof(settingsTab));
    }

    if (previousSelectedTopLevelTab is GameHistoryTab previousGameTab)
    {
        GameHistoryTab matchingTab = refreshedTabs?.FirstOrDefault(tab =>
            string.Equals(tab.GameName, previousGameTab.GameName, StringComparison.OrdinalIgnoreCase));
        if (matchingTab != null)
        {
            return matchingTab;
        }
    }

    if (previousSelectedTopLevelTab is StatsPlusSettingsTab)
    {
        return settingsTab;
    }

    return refreshedTabs?.FirstOrDefault() ?? (object)settingsTab;
}
```

Revise `RefreshStoredTrackSummaries()` so `Apply()` captures the previous selected top-level tab before clearing collections:

```csharp
object previousSelectedTopLevelTab = SelectedTopLevelTab;
string selectedGame = SelectedTrackSummary?.GameName;
string selectedCar = SelectedTrackSummary?.CarModel;
string selectedTrackConfig = SelectedTrackSummary?.TrackNameWithConfig;

GameHistoryTabs.Clear();
TopLevelTabs.Clear();
foreach (GameHistoryTab tab in tabs)
{
    GameHistoryTabs.Add(tab);
    TopLevelTabs.Add(tab);
}

TopLevelTabs.Add(_settingsTab);

SelectedTopLevelTab = ResolveSelectedTopLevelTab(previousSelectedTopLevelTab, tabs, _settingsTab);
```

Keep the existing `SelectedTrackSummary = summaries.FirstOrDefault(...)` assignment immediately after tab selection. That preserves the selected car/track combination when the row still exists and clears it when it does not.

- [ ] **Step 4: Run the tab layout tests and verify they pass**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter "FullyQualifiedName~StatsPlusPluginTabLayoutTests"
```

Expected: PASS.

- [ ] **Step 5: Commit Task 2**

Run:

```powershell
git status --short --branch
git add -- StatsPlus/StatsPlusPlugin.cs StatsPlus.Tests/StatsPlusPluginTabLayoutTests.cs
git commit -m "Preserve StatsPlus game tab selection"
```

Expected: commit created on the current feature branch, not on `main`.

---

### Task 3: Live Status State And Color Bindings

**Files:**
- Modify: `StatsPlus/StatsPlusPlugin.cs`
- Create: `StatsPlus.Tests/StatsPlusPluginLiveStatusTests.cs`

**Interfaces:**
- Consumes: existing `StatsPlusPlugin.DataStatus`, `StatsPlusPlugin.DataUpdate(...)`, `PluginSettings.EnablePlugin`, `PluginSettings.RecordLeMansUltimate`.
- Produces:
  - `StatsPlusPlugin.IsTelemetryActive : bool`
  - `StatsPlusPlugin.LiveStatusLabel : string`
  - `StatsPlusPlugin.StatusSectionForeground : Brush`

- [ ] **Step 1: Write failing live status tests**

Create `StatsPlus.Tests/StatsPlusPluginLiveStatusTests.cs` with this content:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using GameReaderCommon;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimHub.Plugins;

namespace StatsPlus.Tests
{
    [TestClass]
    public class StatsPlusPluginLiveStatusTests
    {
        private string _tempDirectory;
        private StatsPlusPlugin _plugin;
        private TestPluginManager _pluginManager;

        [TestInitialize]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "StatsPlusPluginLiveStatusTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        [TestCleanup]
        public void TearDown()
        {
            _plugin?.End(_pluginManager);
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        [TestMethod]
        public void Init_LiveStatusDefaultsToStandbyRed()
        {
            StatsPlusPlugin plugin = CreateInitializedPlugin();

            Assert.IsFalse(plugin.IsTelemetryActive);
            Assert.AreEqual("Standby", plugin.LiveStatusLabel);
            Assert.AreSame(Brushes.Red, plugin.StatusSectionForeground);
        }

        [TestMethod]
        public void DataUpdate_WithSupportedRunningTelemetry_SetsRecordingGreen()
        {
            StatsPlusPlugin plugin = CreateInitializedPlugin();
            var gameData = CreateGameData(gameRunning: true, gameName: "LMU");

            plugin.DataUpdate(_pluginManager, ref gameData);

            Assert.IsTrue(plugin.IsTelemetryActive);
            Assert.AreEqual("Recording", plugin.LiveStatusLabel);
            Assert.AreSame(Brushes.LimeGreen, plugin.StatusSectionForeground);
            Assert.AreEqual("Recording telemetry", plugin.DataStatus);
        }

        [TestMethod]
        public void DataUpdate_WhenPluginDisabled_SetsStandbyRed()
        {
            StatsPlusPlugin plugin = CreateInitializedPlugin();
            plugin.Settings.EnablePlugin = false;
            var gameData = CreateGameData(gameRunning: true, gameName: "LMU");

            plugin.DataUpdate(_pluginManager, ref gameData);

            Assert.IsFalse(plugin.IsTelemetryActive);
            Assert.AreEqual("Standby", plugin.LiveStatusLabel);
            Assert.AreSame(Brushes.Red, plugin.StatusSectionForeground);
            Assert.AreEqual("Plugin disabled", plugin.DataStatus);
        }

        [TestMethod]
        public void DataUpdate_WhenGameRecordingDisabled_SetsStandbyRed()
        {
            StatsPlusPlugin plugin = CreateInitializedPlugin();
            plugin.Settings.RecordLeMansUltimate = false;
            var gameData = CreateGameData(gameRunning: true, gameName: "LMU");

            plugin.DataUpdate(_pluginManager, ref gameData);

            Assert.IsFalse(plugin.IsTelemetryActive);
            Assert.AreEqual("Standby", plugin.LiveStatusLabel);
            Assert.AreSame(Brushes.Red, plugin.StatusSectionForeground);
            Assert.AreEqual("Recording disabled for LMU", plugin.DataStatus);
        }

        private StatsPlusPlugin CreateInitializedPlugin()
        {
            _plugin = new StatsPlusPlugin();
            _pluginManager = new TestPluginManager(_tempDirectory);
            _plugin.Init(_pluginManager);
            return _plugin;
        }

        private static GameData CreateGameData(bool gameRunning, string gameName)
        {
            return new GameData
            {
                GameRunning = gameRunning,
                GameName = gameName,
                OldData = new TestStatusData(),
                NewData = new TestStatusData
                {
                    CarModel = "Ferrari 499P",
                    TrackName = "Le Mans",
                    TrackNameWithConfig = "Le Mans - 24h",
                    CompletedLaps = 0,
                    LastLapTime = null,
                    IsLapValid = true
                }
            };
        }

        private sealed class TestStatusData : StatusDataBase
        {
            public override object GetRawDataObject()
            {
                return new object();
            }
        }

        private sealed class TestPluginManager : PluginManager
        {
            private readonly string _commonStorageRoot;

            public TestPluginManager(string tempDirectory)
            {
                _commonStorageRoot = Path.Combine(tempDirectory, "PluginsData", "Common");
            }

            public Dictionary<string, object> Properties { get; } = new Dictionary<string, object>();

            public override string GetCommonStoragePath(params string[] pathParts)
            {
                Directory.CreateDirectory(_commonStorageRoot);
                return Path.Combine(new[] { _commonStorageRoot }.Concat(pathParts ?? Array.Empty<string>()).ToArray());
            }

            public override string GetCommonStoragePath(bool create, params string[] pathParts)
            {
                return GetCommonStoragePath(pathParts);
            }

            public override void AddProperty<T>(string propertyName, Type ownerType, T initialValue, string unit = null)
            {
                Properties[propertyName] = initialValue;
            }

            public override void SetPropertyValue(string propertyName, Type ownerType, object value)
            {
                Properties[propertyName] = value;
            }
        }
    }
}
```

- [ ] **Step 2: Run the live status tests and verify failures**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter "FullyQualifiedName~StatsPlusPluginLiveStatusTests"
```

Expected: FAIL because `IsTelemetryActive`, `LiveStatusLabel`, and `StatusSectionForeground` do not exist.

- [ ] **Step 3: Implement live status properties**

In `StatsPlus/StatsPlusPlugin.cs`, add a field near `_dataStatus`:

```csharp
private bool _isTelemetryActive;
```

Add properties near `DataStatus`:

```csharp
public string LiveStatusLabel => IsTelemetryActive ? "Recording" : "Standby";

public Brush StatusSectionForeground => IsTelemetryActive ? Brushes.LimeGreen : Brushes.Red;

public bool IsTelemetryActive
{
    get => _isTelemetryActive;
    private set
    {
        if (_isTelemetryActive == value)
        {
            return;
        }

        _isTelemetryActive = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(LiveStatusLabel));
        OnPropertyChanged(nameof(StatusSectionForeground));
    }
}
```

- [ ] **Step 4: Update DataUpdate state transitions**

In `DataUpdate(...)`, set `IsTelemetryActive = false` before each early return where StatsPlus is not actively recording:

```csharp
if (!Settings.EnablePlugin || !data.GameRunning || data.NewData == null)
{
    DataStatus = !Settings.EnablePlugin ? "Plugin disabled" : "Waiting for telemetry";
    IsTelemetryActive = false;
    ClearLiveTelemetryProperties(pluginManager);
    _hasLoggedDataError = false;
    return;
}
```

```csharp
if (!IsGameRecordingEnabled(gameName))
{
    DataStatus = $"Recording disabled for {gameName}";
    IsTelemetryActive = false;
    ClearLiveTelemetryProperties(pluginManager);
    _hasLoggedDataError = false;
    return;
}
```

After the game is enabled and before switching context, keep the status red if persistent lap storage is unavailable:

```csharp
if (!HasLapRepository)
{
    DataStatus = "Lap storage unavailable";
    IsTelemetryActive = false;
    ClearLiveTelemetryProperties(pluginManager);
    _hasLoggedDataError = false;
    return;
}
```

After the context is normalized, game recording is enabled, and lap storage is available, set active state before final status publishing:

```csharp
IsTelemetryActive = true;
```

Keep `DataStatus = "Recording telemetry";` at the end of the normal path. In the `catch` block, set `IsTelemetryActive = false` before logging/returning because the plugin is no longer confidently processing telemetry:

```csharp
IsTelemetryActive = false;
```

In `InitializeDatabase()` catch, set:

```csharp
DataStatus = "Lap storage unavailable";
IsTelemetryActive = false;
```

- [ ] **Step 5: Run live status tests and verify they pass**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter "FullyQualifiedName~StatsPlusPluginLiveStatusTests"
```

Expected: PASS.

- [ ] **Step 6: Commit Task 3**

Run:

```powershell
git status --short --branch
git add -- StatsPlus/StatsPlusPlugin.cs StatsPlus.Tests/StatsPlusPluginLiveStatusTests.cs
git commit -m "Add StatsPlus live status state"
```

Expected: commit created on the current feature branch, not on `main`.

---

### Task 4: XAML Layout With Fixed Live Status Bar And Dynamic Tabs

**Files:**
- Modify: `StatsPlus/SettingsControl.xaml`
- Keep: `StatsPlus/SettingsControl.xaml.cs`

**Interfaces:**
- Consumes:
  - `TopLevelTabs`
  - `SelectedTopLevelTab`
  - `LiveStatusLabel`
  - `StatusSectionForeground`
  - `DataStatus`
  - `CurrentContext`
  - `SessionLapCount`
  - `LastLapSeconds`
  - `SessionBestLapSeconds`
  - `AllTimeBestLapSeconds`
  - `GameHistoryTab.Tracks`
  - `SelectedTrackSummary`
  - `SelectedTrackLaps`
  - `SelectedLap`
  - `StatsPlusSettingsTab`
- Produces: a two-row root layout where the live status bar stays above the scrollable dynamic tab content.

- [ ] **Step 1: Replace the fixed `ScrollViewer` root with a two-row `Grid`**

In `StatsPlus/SettingsControl.xaml`, keep the existing `UserControl` attributes and resources, then replace the current root `ScrollViewer` with:

```xml
<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="*" />
    </Grid.RowDefinitions>

    <Border Grid.Row="0"
            Margin="0,0,0,8"
            Padding="12,10"
            Background="#252525"
            BorderBrush="#2CB7F0"
            BorderThickness="1"
            CornerRadius="4">
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="120" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="120" />
                <ColumnDefinition Width="120" />
                <ColumnDefinition Width="120" />
                <ColumnDefinition Width="120" />
            </Grid.ColumnDefinitions>

            <StackPanel Grid.Column="0">
                <TextBlock Foreground="#89D9FF" FontSize="12" FontWeight="SemiBold" Text="Live status" />
                <TextBlock Foreground="{Binding StatusSectionForeground}" FontSize="18" FontWeight="Bold" Text="{Binding LiveStatusLabel}" TextTrimming="CharacterEllipsis" />
            </StackPanel>

            <StackPanel Grid.Column="1" Margin="16,0,16,0">
                <TextBlock Foreground="{Binding StatusSectionForeground}" FontSize="12" FontWeight="SemiBold" Text="{Binding DataStatus}" TextTrimming="CharacterEllipsis" />
                <TextBlock Margin="0,4,0,0" Foreground="#D8D8D8" FontSize="12" Text="{Binding CurrentContext}" TextTrimming="CharacterEllipsis" />
            </StackPanel>

            <StackPanel Grid.Column="2">
                <TextBlock Foreground="#89D9FF" FontSize="12" FontWeight="SemiBold" Text="Session laps" />
                <TextBlock Foreground="#D8D8D8" FontSize="18" FontWeight="Bold" Text="{Binding SessionLapCount}" />
            </StackPanel>

            <StackPanel Grid.Column="3">
                <TextBlock Foreground="#89D9FF" FontSize="12" FontWeight="SemiBold" Text="Last" />
                <TextBlock Foreground="#D8D8D8" FontSize="18" FontWeight="Bold" Text="{Binding LastLapSeconds, Converter={StaticResource TimeSpanSecondsFormatter}}" />
            </StackPanel>

            <StackPanel Grid.Column="4">
                <TextBlock Foreground="#89D9FF" FontSize="12" FontWeight="SemiBold" Text="Session best" />
                <TextBlock Foreground="#D8D8D8" FontSize="18" FontWeight="Bold" Text="{Binding SessionBestLapSeconds, Converter={StaticResource TimeSpanSecondsFormatter}}" />
            </StackPanel>

            <StackPanel Grid.Column="5">
                <TextBlock Foreground="#89D9FF" FontSize="12" FontWeight="SemiBold" Text="All-time best" />
                <TextBlock Foreground="#D8D8D8" FontSize="18" FontWeight="Bold" Text="{Binding AllTimeBestLapSeconds, Converter={StaticResource TimeSpanSecondsFormatter}}" />
            </StackPanel>
        </Grid>
    </Border>

    <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
        <styles:SHTabControl ItemsSource="{Binding TopLevelTabs}"
                             SelectedItem="{Binding SelectedTopLevelTab, Mode=TwoWay}">
            <styles:SHTabControl.ItemTemplate>
                <DataTemplate>
                    <TextBlock Text="{Binding Header}" />
                </DataTemplate>
            </styles:SHTabControl.ItemTemplate>

            <styles:SHTabControl.Resources>
            </styles:SHTabControl.Resources>
        </styles:SHTabControl>
    </ScrollViewer>
</Grid>
```

- [ ] **Step 2: Add data-template-driven top-level `SHTabControl`**

Inside the `ScrollViewer`, use this `SHTabControl` structure if it was not already added in Step 1:

```xml
<styles:SHTabControl ItemsSource="{Binding TopLevelTabs}"
                     SelectedItem="{Binding SelectedTopLevelTab, Mode=TwoWay}">
    <styles:SHTabControl.ItemTemplate>
        <DataTemplate>
            <TextBlock Text="{Binding Header}" />
        </DataTemplate>
    </styles:SHTabControl.ItemTemplate>

    <styles:SHTabControl.Resources>
    </styles:SHTabControl.Resources>
</styles:SHTabControl>
```

- [ ] **Step 3: Move current history UI into the `GameHistoryTab` template**

Inside `styles:SHTabControl.Resources`, add a `GameHistoryTab` template. Preserve the current columns and handlers:

```xml
<DataTemplate DataType="{x:Type local:GameHistoryTab}">
    <StackPanel>
        <styles:SHSection Title="Stored History">
            <StackPanel Margin="12">
                <StackPanel Orientation="Horizontal" Margin="0,0,0,10">
                    <Button Width="140"
                            Margin="0,0,10,0"
                            HorizontalAlignment="Left"
                            Click="RefreshHistoryButton_Click"
                            Content="Refresh history" />
                    <Button Width="150"
                            Margin="0,0,10,0"
                            HorizontalAlignment="Left"
                            IsEnabled="{Binding DataContext.HasSelectedGameHistoryTab, ElementName=Root}"
                            Click="ClearSelectedGameButton_Click"
                            Content="Clear selected game" />
                    <Button Width="120"
                            HorizontalAlignment="Left"
                            Click="ClearAllDataButton_Click"
                            Content="Clear all data" />
                </StackPanel>

                <DataGrid ItemsSource="{Binding Tracks}"
                          AutoGenerateColumns="False"
                          CanUserAddRows="False"
                          IsReadOnly="True"
                          Height="300"
                          SelectedItem="{Binding DataContext.SelectedTrackSummary, ElementName=Root, Mode=TwoWay}">
                    <DataGrid.Columns>
                        <DataGridTextColumn Header="Car" Binding="{Binding CarModelDisplay}" Width="180" />
                        <DataGridTextColumn Header="Track" Binding="{Binding TrackName}" Width="120" />
                        <DataGridTextColumn Header="Variation" Binding="{Binding TrackNameWithConfigDisplay}" Width="180" />
                        <DataGridTextColumn Header="Laps" Binding="{Binding LapCount}" Width="60" />
                        <DataGridTextColumn Header="Best" Binding="{Binding BestLapSeconds, Converter={StaticResource TimeSpanSecondsFormatter}}" Width="90" />
                        <DataGridTextColumn Header="Last UTC" Binding="{Binding LastRecordedUtc, StringFormat={}{0:u}}" Width="150" />
                    </DataGrid.Columns>
                </DataGrid>
            </StackPanel>
        </styles:SHSection>

        <styles:SHSection Title="Recorded Laps">
            <StackPanel Margin="12">
                <TextBlock Margin="0,0,0,8"
                           FontWeight="Bold"
                           Text="{Binding DataContext.SelectedTrackCaption, ElementName=Root}" />
                <Button Width="170"
                        Margin="0,0,0,10"
                        HorizontalAlignment="Left"
                        IsEnabled="{Binding DataContext.HasSelectedLap, ElementName=Root}"
                        Click="ToggleLapValidityButton_Click"
                        Content="Toggle selected lap valid" />
                <DataGrid ItemsSource="{Binding DataContext.SelectedTrackLaps, ElementName=Root}"
                          AutoGenerateColumns="False"
                          CanUserAddRows="False"
                          IsReadOnly="True"
                          Height="240"
                          SelectedItem="{Binding DataContext.SelectedLap, ElementName=Root, Mode=TwoWay}">
                    <DataGrid.Columns>
                        <DataGridTextColumn Header="Lap" Binding="{Binding LapNumber}" Width="55" />
                        <DataGridTextColumn Header="Time" Binding="{Binding LapTimeSeconds, Converter={StaticResource TimeSpanSecondsFormatter}}" Width="90" />
                        <DataGridTextColumn Header="S1" Binding="{Binding Sector1Seconds, Converter={StaticResource TimeSpanSecondsFormatter}}" Width="80" />
                        <DataGridTextColumn Header="S2" Binding="{Binding Sector2Seconds, Converter={StaticResource TimeSpanSecondsFormatter}}" Width="80" />
                        <DataGridTextColumn Header="S3" Binding="{Binding Sector3Seconds, Converter={StaticResource TimeSpanSecondsFormatter}}" Width="80" />
                        <DataGridCheckBoxColumn Header="Valid" Binding="{Binding IsValid}" Width="60" />
                        <DataGridTextColumn Header="Recorded UTC" Binding="{Binding TimestampUtc, StringFormat={}{0:u}}" Width="160" />
                    </DataGrid.Columns>
                </DataGrid>
            </StackPanel>
        </styles:SHSection>
    </StackPanel>
</DataTemplate>
```

Ensure the root `UserControl` has `x:Name="Root"` so `ElementName=Root` bindings work:

```xml
<UserControl x:Class="StatsPlus.SettingsControl"
             x:Name="Root"
```

- [ ] **Step 4: Move current settings UI into the `StatsPlusSettingsTab` template**

Inside `styles:SHTabControl.Resources`, add:

```xml
<DataTemplate DataType="{x:Type local:StatsPlusSettingsTab}">
    <StackPanel>
        <styles:SHSection Title="General">
            <StackPanel Margin="12">
                <Grid Margin="0,0,0,10">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="190" />
                        <ColumnDefinition Width="*" />
                    </Grid.ColumnDefinitions>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="Auto" />
                    </Grid.RowDefinitions>

                    <TextBlock Grid.Row="0"
                               Grid.Column="0"
                               Margin="0,0,12,8"
                               VerticalAlignment="Center"
                               Text="Enable plugin" />
                    <CheckBox Grid.Row="0"
                              Grid.Column="1"
                              Margin="0,0,0,8"
                              HorizontalAlignment="Left"
                              IsChecked="{Binding DataContext.Settings.EnablePlugin, ElementName=Root, Mode=TwoWay}" />

                    <TextBlock Grid.Row="1"
                               Grid.Column="0"
                               Margin="0,0,12,8"
                               VerticalAlignment="Center"
                               Text="Publish game / track / car" />
                    <CheckBox Grid.Row="1"
                              Grid.Column="1"
                              Margin="0,0,0,8"
                              HorizontalAlignment="Left"
                              IsChecked="{Binding DataContext.Settings.PublishTrackInfo, ElementName=Root, Mode=TwoWay}" />

                    <TextBlock Grid.Row="2"
                               Grid.Column="0"
                               Margin="0,0,12,0"
                               VerticalAlignment="Center"
                               Text="Custom label" />
                    <TextBox Grid.Row="2"
                             Grid.Column="1"
                             Width="220"
                             HorizontalAlignment="Left"
                             Text="{Binding DataContext.Settings.CustomLabel, ElementName=Root, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
                </Grid>

                <StackPanel Orientation="Horizontal">
                    <Button Width="120"
                            Margin="0,0,10,0"
                            Click="SaveButton_Click"
                            Content="Save settings" />
                    <Button Width="140"
                            Click="ResetButton_Click"
                            Content="Reset defaults" />
                </StackPanel>
            </StackPanel>
        </styles:SHSection>

        <styles:SHSection Title="Enabled Games">
            <StackPanel Margin="12">
                <TextBlock Margin="0,0,0,10" TextWrapping="Wrap">
                    StatsPlus only records laps for the enabled games below. Unsupported or disabled games are ignored.
                </TextBlock>

                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="220" />
                        <ColumnDefinition Width="*" />
                    </Grid.ColumnDefinitions>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="Auto" />
                    </Grid.RowDefinitions>

                    <TextBlock Grid.Row="0" Grid.Column="0" Margin="0,0,12,8" VerticalAlignment="Center" Text="Assetto Corsa" />
                    <CheckBox Grid.Row="0" Grid.Column="1" Margin="0,0,0,8" HorizontalAlignment="Left" IsChecked="{Binding DataContext.Settings.RecordAssettoCorsa, ElementName=Root, Mode=TwoWay}" />

                    <TextBlock Grid.Row="1" Grid.Column="0" Margin="0,0,12,8" VerticalAlignment="Center" Text="Assetto Corsa EVO" />
                    <CheckBox Grid.Row="1" Grid.Column="1" Margin="0,0,0,8" HorizontalAlignment="Left" IsChecked="{Binding DataContext.Settings.RecordAssettoCorsaEvo, ElementName=Root, Mode=TwoWay}" />

                    <TextBlock Grid.Row="2" Grid.Column="0" Margin="0,0,12,8" VerticalAlignment="Center" Text="Automobilista 2" />
                    <CheckBox Grid.Row="2" Grid.Column="1" Margin="0,0,0,8" HorizontalAlignment="Left" IsChecked="{Binding DataContext.Settings.RecordAutomobilista2, ElementName=Root, Mode=TwoWay}" />

                    <TextBlock Grid.Row="3" Grid.Column="0" Margin="0,0,12,8" VerticalAlignment="Center" Text="iRacing" />
                    <CheckBox Grid.Row="3" Grid.Column="1" Margin="0,0,0,8" HorizontalAlignment="Left" IsChecked="{Binding DataContext.Settings.RecordIRacing, ElementName=Root, Mode=TwoWay}" />

                    <TextBlock Grid.Row="4" Grid.Column="0" Margin="0,0,12,8" VerticalAlignment="Center" Text="Le Mans Ultimate" />
                    <CheckBox Grid.Row="4" Grid.Column="1" Margin="0,0,0,8" HorizontalAlignment="Left" IsChecked="{Binding DataContext.Settings.RecordLeMansUltimate, ElementName=Root, Mode=TwoWay}" />

                    <TextBlock Grid.Row="5" Grid.Column="0" Margin="0,0,12,8" VerticalAlignment="Center" Text="rFactor 2" />
                    <CheckBox Grid.Row="5" Grid.Column="1" Margin="0,0,0,8" HorizontalAlignment="Left" IsChecked="{Binding DataContext.Settings.RecordRFactor2, ElementName=Root, Mode=TwoWay}" />

                    <TextBlock Grid.Row="6" Grid.Column="0" Margin="0,0,12,0" VerticalAlignment="Center" Text="RaceRoom / RRRE" />
                    <CheckBox Grid.Row="6" Grid.Column="1" Margin="0,0,0,0" HorizontalAlignment="Left" IsChecked="{Binding DataContext.Settings.RecordR3E, ElementName=Root, Mode=TwoWay}" />
                </Grid>
            </StackPanel>
        </styles:SHSection>

        <styles:SHSection Title="About">
            <StackPanel Margin="12">
                <TextBlock FontWeight="Bold" Text="StatsPlus" />
                <TextBlock Margin="0,6,0,0" TextWrapping="Wrap">
                    Records lap and sector times for the active game using a local LiteDB lap history store.
                </TextBlock>
                <TextBlock Margin="0,6,0,0" TextWrapping="Wrap">
                    Published properties: StatsPlus.Version, StatsPlus.Enabled, StatsPlus.Label, StatsPlus.GameName,
                    StatsPlus.TrackName, StatsPlus.CarModel, StatsPlus.SpeedKmh, StatsPlus.IsGameRunning,
                    StatsPlus.LastLapTime, StatsPlus.SessionBestLapTime, StatsPlus.AllTimeBestLapTime,
                    StatsPlus.SessionLapCount, StatsPlus.LastSector1Time, StatsPlus.LastSector2Time, StatsPlus.LastSector3Time
                </TextBlock>
                <TextBlock Margin="0,6,0,0" TextWrapping="Wrap">
                    Stored personal bests are also exposed as StatsPlus.PersonalBest.{Game}.{Car}.{TrackVariation},
                    using sanitized game, car, and TrackNameWithConfig segments for each recorded combination.
                </TextBlock>
            </StackPanel>
        </styles:SHSection>
    </StackPanel>
</DataTemplate>
```

Use the `ElementName=Root` binding pattern for all settings bindings because the template data context is `StatsPlusSettingsTab`, not `StatsPlusPlugin`.

- [ ] **Step 5: Build to verify XAML compiles**

Run:

```powershell
dotnet build StatsPlus.sln /p:SimHubInstallPath=C:\does-not-exist
```

Expected: PASS. If a XAML binding compile error names `Root`, confirm `x:Name="Root"` is present on `UserControl`. If a binding compile error names `Settings`, add `DataContext.` and `ElementName=Root` to that binding.

- [ ] **Step 6: Commit Task 4**

Run:

```powershell
git status --short --branch
git add -- StatsPlus/SettingsControl.xaml
git commit -m "Redesign StatsPlus settings layout"
```

Expected: commit created on the current feature branch, not on `main`.

---

### Task 5: Full Verification And Final Fixes

**Files:**
- Modify only files touched by Tasks 1-4 if verification finds failures.

**Interfaces:**
- Consumes: all task outputs.
- Produces: a verified feature branch ready for review.

- [ ] **Step 1: Run all StatsPlus tests**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj
```

Expected: PASS.

- [ ] **Step 2: Run full solution build without deploying to SimHub**

Run:

```powershell
dotnet build StatsPlus.sln /p:SimHubInstallPath=C:\does-not-exist
```

Expected: PASS.

- [ ] **Step 3: Inspect final diff for scope**

Run:

```powershell
git status --short --branch
git diff --stat main...HEAD
git diff --name-only main...HEAD
```

Expected changed files are limited to:

```text
docs/superpowers/specs/2026-07-31-affinity-style-game-tabs-design.md
docs/superpowers/plans/2026-07-31-affinity-style-game-tabs.md
StatsPlus/LapHistoryModels.cs
StatsPlus/StatsPlusPlugin.cs
StatsPlus/SettingsControl.xaml
StatsPlus.Tests/StatsPlusPluginTabLayoutTests.cs
StatsPlus.Tests/StatsPlusPluginLiveStatusTests.cs
```

- [ ] **Step 4: Commit any verification fixes**

If Steps 1 or 2 required code changes, run:

```powershell
git status --short --branch
git add -- StatsPlus/LapHistoryModels.cs StatsPlus/StatsPlusPlugin.cs StatsPlus/SettingsControl.xaml StatsPlus.Tests/StatsPlusPluginTabLayoutTests.cs StatsPlus.Tests/StatsPlusPluginLiveStatusTests.cs
git commit -m "Fix StatsPlus game tab verification issues"
```

Expected: no commit is created if there were no verification fixes; otherwise the commit is on the current feature branch, not on `main`.

- [ ] **Step 5: Prepare review summary**

Collect these outputs for the final response or PR description:

```powershell
git log --oneline main..HEAD
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj
dotnet build StatsPlus.sln /p:SimHubInstallPath=C:\does-not-exist
```

Expected: final summary includes the branch name, changed UI behavior, live status color behavior, and test/build results.
