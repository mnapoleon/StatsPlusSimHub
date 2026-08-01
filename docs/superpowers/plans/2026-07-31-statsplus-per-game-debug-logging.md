# StatsPlus Per-Game Debug Logging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Affinity-style global and per-game diagnostic logging controls to StatsPlus, with separate diagnostic log files per enabled game.

**Architecture:** Extend `PluginSettings` with debug logging state, expose per-game options from `StatsPlusPlugin`, and route existing centralized diagnostic writes through a game-aware gate. Keep the implementation close to Affinity's pattern while reusing StatsPlus' current `WriteDiagnosticLog` call sites and settings tab.

**Tech Stack:** C# `net48`, WPF/XAML, MSTest, Newtonsoft.Json, SimHub plugin SDK stubs.

## Global Constraints

- StatsPlus mirrors Affinity's debug logging model.
- Diagnostic logging is globally disabled by default.
- Each supported game's diagnostic logging entry is disabled by default.
- Enabled diagnostics are written to separate files per game.
- RaceRoom aliases `r3e` and `rrre` normalize to `raceroomracingexperience`.
- This change does not alter lap recording toggles, lap storage, tab layout, or published SimHub properties except any diagnostic path property explicitly needed by existing StatsPlus behavior.

---

## File Structure

- Create `StatsPlus/GameDebugLoggingOption.cs`: small WPF binding view model for one per-game debug logging checkbox.
- Modify `StatsPlus/PluginSettings.cs`: persist global and per-game logging settings.
- Modify `StatsPlus/StatsPlusPlugin.cs`: expose binding properties, normalize logging keys, populate options, gate and route diagnostic writes.
- Modify `StatsPlus/SettingsControl.xaml`: add the settings UI section.
- Create `StatsPlus.Tests/StatsPlusDebugLoggingSettingsTests.cs`: settings/default/options tests.
- Create `StatsPlus.Tests/StatsPlusDiagnosticLoggingTests.cs`: file path and write-gating tests.

### Task 1: Settings Model And Per-Game Options

**Files:**
- Create: `StatsPlus/GameDebugLoggingOption.cs`
- Create: `StatsPlus.Tests/StatsPlusDebugLoggingSettingsTests.cs`
- Modify: `StatsPlus/PluginSettings.cs`
- Modify: `StatsPlus/StatsPlusPlugin.cs`

**Interfaces:**
- Produces: `PluginSettings.EnableDebugLogging : bool`
- Produces: `PluginSettings.GameDebugLogging : Dictionary<string, bool>`
- Produces: `StatsPlusPlugin.IsDebugLoggingEnabled : bool`
- Produces: `StatsPlusPlugin.GameDebugLoggingOptions : ObservableCollection<GameDebugLoggingOption>`
- Produces private helpers:
  - `EnsureDefaultGameDebugLoggingSettings() : void`
  - `EnsureGameDebugLoggingConfigured(string gameName) : bool`
  - `RefreshGameDebugLoggingOptions() : void`
  - `UpdateGameDebugLoggingSetting(string settingsKey, bool isEnabled) : void`
  - `GetDebugLoggingSettingsKey(string gameName) : string`
  - `GetDebugLoggingDisplayName(string settingsKey) : string`
  - `IsSupportedDebugLoggingSettingsKey(string settingsKey) : bool`

- [ ] **Step 1: Write failing settings tests**

Create `StatsPlus.Tests/StatsPlusDebugLoggingSettingsTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace StatsPlus.Tests
{
    [TestClass]
    public class StatsPlusDebugLoggingSettingsTests
    {
        [TestMethod]
        public void NewSettings_DisablesDebugLoggingByDefault()
        {
            PluginSettings settings = new PluginSettings();

            Assert.IsFalse(settings.EnableDebugLogging);
            Assert.IsNotNull(settings.GameDebugLogging);
            Assert.AreEqual(0, settings.GameDebugLogging.Count);
        }

        [TestMethod]
        public void Reset_DisablesDebugLoggingAndClearsGameSelections()
        {
            PluginSettings settings = new PluginSettings
            {
                EnableDebugLogging = true,
                GameDebugLogging = new Dictionary<string, bool>
                {
                    ["iracing"] = true
                }
            };

            settings.Reset();

            Assert.IsFalse(settings.EnableDebugLogging);
            Assert.IsNotNull(settings.GameDebugLogging);
            Assert.AreEqual(0, settings.GameDebugLogging.Count);
        }

        [TestMethod]
        public void EnsureDefaultGameDebugLoggingSettings_AddsSupportedGamesDisabled()
        {
            StatsPlusPlugin plugin = new StatsPlusPlugin();
            MethodInfo method = typeof(StatsPlusPlugin).GetMethod(
                "EnsureDefaultGameDebugLoggingSettings",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(method);
            method.Invoke(plugin, null);

            Assert.IsTrue(plugin.Settings.GameDebugLogging.ContainsKey("assettocorsa"));
            Assert.IsTrue(plugin.Settings.GameDebugLogging.ContainsKey("assettocorsaevo"));
            Assert.IsTrue(plugin.Settings.GameDebugLogging.ContainsKey("automobilista2"));
            Assert.IsTrue(plugin.Settings.GameDebugLogging.ContainsKey("iracing"));
            Assert.IsTrue(plugin.Settings.GameDebugLogging.ContainsKey("lmu"));
            Assert.IsTrue(plugin.Settings.GameDebugLogging.ContainsKey("rfactor2"));
            Assert.IsTrue(plugin.Settings.GameDebugLogging.ContainsKey("raceroomracingexperience"));
            Assert.IsFalse(plugin.Settings.GameDebugLogging.Values.Any(isEnabled => isEnabled));
        }

        [TestMethod]
        public void RefreshGameDebugLoggingOptions_RendersFriendlyLabelsUnchecked()
        {
            StatsPlusPlugin plugin = new StatsPlusPlugin();
            Invoke(plugin, "EnsureDefaultGameDebugLoggingSettings");
            Invoke(plugin, "RefreshGameDebugLoggingOptions");

            GameDebugLoggingOption option = plugin.GameDebugLoggingOptions
                .Single(entry => entry.SettingsKey == "raceroomracingexperience");

            Assert.AreEqual("RaceRoom Racing Experience", option.DisplayName);
            Assert.IsFalse(option.IsEnabled);
        }

        [TestMethod]
        public void GameDebugLoggingOption_UpdateChangesSettingsDictionary()
        {
            StatsPlusPlugin plugin = new StatsPlusPlugin();
            Invoke(plugin, "EnsureDefaultGameDebugLoggingSettings");
            Invoke(plugin, "RefreshGameDebugLoggingOptions");

            GameDebugLoggingOption option = plugin.GameDebugLoggingOptions
                .Single(entry => entry.SettingsKey == "iracing");

            option.IsEnabled = true;

            Assert.IsTrue(plugin.Settings.GameDebugLogging["iracing"]);
        }

        [TestMethod]
        public void GetDebugLoggingSettingsKey_NormalizesRaceRoomAliases()
        {
            StatsPlusPlugin plugin = new StatsPlusPlugin();
            MethodInfo method = typeof(StatsPlusPlugin).GetMethod(
                "GetDebugLoggingSettingsKey",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.AreEqual("raceroomracingexperience", method.Invoke(plugin, new object[] { "R3E" }));
            Assert.AreEqual("raceroomracingexperience", method.Invoke(plugin, new object[] { "RRRE" }));
            Assert.AreEqual("raceroomracingexperience", method.Invoke(plugin, new object[] { "RaceRoom Racing Experience" }));
        }

        [TestMethod]
        public void IsDebugLoggingEnabled_UpdatesSettingAndRaisesPropertyChanged()
        {
            StatsPlusPlugin plugin = new StatsPlusPlugin();
            List<string> changedProperties = new List<string>();
            plugin.PropertyChanged += (sender, args) => changedProperties.Add(args.PropertyName);

            plugin.IsDebugLoggingEnabled = true;
            plugin.IsDebugLoggingEnabled = false;

            CollectionAssert.AreEqual(
                new[] { nameof(StatsPlusPlugin.IsDebugLoggingEnabled), nameof(StatsPlusPlugin.IsDebugLoggingEnabled) },
                changedProperties.Where(name => name == nameof(StatsPlusPlugin.IsDebugLoggingEnabled)).ToList());
            Assert.IsFalse(plugin.Settings.EnableDebugLogging);
        }

        private static void Invoke(StatsPlusPlugin plugin, string methodName)
        {
            typeof(StatsPlusPlugin)
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(plugin, null);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test .\StatsPlus.Tests\StatsPlus.Tests.csproj --filter StatsPlusDebugLoggingSettingsTests`

Expected: compile failure because `EnableDebugLogging`, `GameDebugLogging`, `GameDebugLoggingOption`, `IsDebugLoggingEnabled`, and the private helper methods do not exist.

- [ ] **Step 3: Implement minimal settings and option code**

Add to `PluginSettings.cs`:

```csharp
using System.Collections.Generic;
```

Add fields and properties:

```csharp
private bool _enableDebugLogging;
private Dictionary<string, bool> _gameDebugLogging = new Dictionary<string, bool>();

public bool EnableDebugLogging
{
    get => _enableDebugLogging;
    set
    {
        if (_enableDebugLogging == value)
        {
            return;
        }

        _enableDebugLogging = value;
        OnPropertyChanged();
    }
}

public Dictionary<string, bool> GameDebugLogging
{
    get => _gameDebugLogging;
    set
    {
        if (ReferenceEquals(_gameDebugLogging, value))
        {
            return;
        }

        _gameDebugLogging = value ?? new Dictionary<string, bool>();
        OnPropertyChanged();
    }
}
```

Add to `Reset()`:

```csharp
EnableDebugLogging = false;
GameDebugLogging = new Dictionary<string, bool>();
```

Create `GameDebugLoggingOption.cs` using Affinity's implementation with namespace changed to `StatsPlus`.

Add to `StatsPlusPlugin.cs`:

```csharp
private static readonly KeyValuePair<string, string>[] DefaultGameDebugLoggingEntries =
{
    new KeyValuePair<string, string>("assettocorsa", "Assetto Corsa"),
    new KeyValuePair<string, string>("assettocorsaevo", "Assetto Corsa EVO"),
    new KeyValuePair<string, string>("automobilista2", "Automobilista 2"),
    new KeyValuePair<string, string>("iracing", "iRacing"),
    new KeyValuePair<string, string>("lmu", "Le Mans Ultimate"),
    new KeyValuePair<string, string>("rfactor2", "rFactor 2"),
    new KeyValuePair<string, string>("raceroomracingexperience", "RaceRoom Racing Experience")
};
```

Add public members:

```csharp
public bool IsDebugLoggingEnabled
{
    get => Settings.EnableDebugLogging;
    set
    {
        if (Settings.EnableDebugLogging == value)
        {
            return;
        }

        Settings.EnableDebugLogging = value;
        OnPropertyChanged();
    }
}

public ObservableCollection<GameDebugLoggingOption> GameDebugLoggingOptions { get; } =
    new ObservableCollection<GameDebugLoggingOption>();
```

Add the private helpers named in this task's interface. Match Affinity's behavior:

```csharp
private void EnsureDefaultGameDebugLoggingSettings()
{
    if (Settings.GameDebugLogging == null)
    {
        Settings.GameDebugLogging = new Dictionary<string, bool>();
    }

    RemoveUnsupportedGameDebugLoggingSettings();

    foreach (KeyValuePair<string, string> entry in DefaultGameDebugLoggingEntries)
    {
        if (!Settings.GameDebugLogging.ContainsKey(entry.Key))
        {
            Settings.GameDebugLogging[entry.Key] = false;
        }
    }
}
```

Call `EnsureDefaultGameDebugLoggingSettings()` and `RefreshGameDebugLoggingOptions()` after `Settings = LoadSettings();` in `Init()`, and call `RefreshGameDebugLoggingOptions()` from `ResetSettings()`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test .\StatsPlus.Tests\StatsPlus.Tests.csproj --filter StatsPlusDebugLoggingSettingsTests`

Expected: all `StatsPlusDebugLoggingSettingsTests` pass.

- [ ] **Step 5: Commit**

Run:

```powershell
git add -- 'StatsPlus/PluginSettings.cs' 'StatsPlus/GameDebugLoggingOption.cs' 'StatsPlus/StatsPlusPlugin.cs' 'StatsPlus.Tests/StatsPlusDebugLoggingSettingsTests.cs'
git commit -m "Add StatsPlus debug logging settings"
```

### Task 2: Diagnostic Log Gating And Per-Game Paths

**Files:**
- Create: `StatsPlus.Tests/StatsPlusDiagnosticLoggingTests.cs`
- Modify: `StatsPlus/StatsPlusPlugin.cs`

**Interfaces:**
- Consumes: `StatsPlusPlugin.GetDebugLoggingSettingsKey(string gameName) : string`
- Produces: `StatsPlusPlugin.ShouldWriteDiagnosticLog(string gameName) : bool`
- Produces: `StatsPlusPlugin.GetDiagnosticLogPath(string gameName) : string`
- Changes: `WriteDiagnosticLog(string gameName, string eventName, string detail) : void`

- [ ] **Step 1: Write failing diagnostic logging tests**

Create `StatsPlus.Tests/StatsPlusDiagnosticLoggingTests.cs`:

```csharp
using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace StatsPlus.Tests
{
    [TestClass]
    public class StatsPlusDiagnosticLoggingTests
    {
        private string _tempDirectory;

        [TestInitialize]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "StatsPlusDiagnosticLoggingTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        [TestCleanup]
        public void TearDown()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        [TestMethod]
        public void WriteDiagnosticLog_GlobalDisabledWritesNoFile()
        {
            StatsPlusPlugin plugin = CreatePluginWithDiagnosticPath();
            plugin.Settings.EnableDebugLogging = false;
            plugin.Settings.GameDebugLogging["iracing"] = true;

            InvokeWriteDiagnosticLog(plugin, "iRacing", "EVENT", "detail");

            Assert.IsFalse(File.Exists(Path.Combine(_tempDirectory, "StatsPlus.diagnostics.iracing.log")));
            Assert.IsFalse(File.Exists(Path.Combine(_tempDirectory, "StatsPlus.diagnostics.log")));
        }

        [TestMethod]
        public void WriteDiagnosticLog_GameDisabledWritesNoFile()
        {
            StatsPlusPlugin plugin = CreatePluginWithDiagnosticPath();
            plugin.Settings.EnableDebugLogging = true;
            plugin.Settings.GameDebugLogging["iracing"] = false;

            InvokeWriteDiagnosticLog(plugin, "iRacing", "EVENT", "detail");

            Assert.IsFalse(File.Exists(Path.Combine(_tempDirectory, "StatsPlus.diagnostics.iracing.log")));
            Assert.IsFalse(File.Exists(Path.Combine(_tempDirectory, "StatsPlus.diagnostics.log")));
        }

        [TestMethod]
        public void WriteDiagnosticLog_GameEnabledWritesGameSpecificFile()
        {
            StatsPlusPlugin plugin = CreatePluginWithDiagnosticPath();
            plugin.Settings.EnableDebugLogging = true;
            plugin.Settings.GameDebugLogging["iracing"] = true;

            InvokeWriteDiagnosticLog(plugin, "iRacing", "EVENT", "detail");

            string gameLogPath = Path.Combine(_tempDirectory, "StatsPlus.diagnostics.iracing.log");
            Assert.IsTrue(File.Exists(gameLogPath));
            StringAssert.Contains(File.ReadAllText(gameLogPath), "EVENT detail");
            Assert.IsFalse(File.Exists(Path.Combine(_tempDirectory, "StatsPlus.diagnostics.log")));
        }

        [TestMethod]
        public void GetDiagnosticLogPath_UsesRaceRoomCanonicalKey()
        {
            StatsPlusPlugin plugin = CreatePluginWithDiagnosticPath();
            MethodInfo method = typeof(StatsPlusPlugin).GetMethod(
                "GetDiagnosticLogPath",
                BindingFlags.Instance | BindingFlags.NonPublic);

            string result = (string)method.Invoke(plugin, new object[] { "R3E" });

            Assert.AreEqual(Path.Combine(_tempDirectory, "StatsPlus.diagnostics.raceroomracingexperience.log"), result);
        }

        private StatsPlusPlugin CreatePluginWithDiagnosticPath()
        {
            StatsPlusPlugin plugin = new StatsPlusPlugin();
            SetDiagnosticLogPath(plugin, Path.Combine(_tempDirectory, "StatsPlus.diagnostics.log"));
            Invoke(plugin, "EnsureDefaultGameDebugLoggingSettings");
            return plugin;
        }

        private static void SetDiagnosticLogPath(StatsPlusPlugin plugin, string path)
        {
            typeof(StatsPlusPlugin)
                .GetField("_diagnosticLogPath", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(plugin, path);
        }

        private static void InvokeWriteDiagnosticLog(StatsPlusPlugin plugin, string gameName, string eventName, string detail)
        {
            typeof(StatsPlusPlugin)
                .GetMethod("WriteDiagnosticLog", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(plugin, new object[] { gameName, eventName, detail });
        }

        private static void Invoke(StatsPlusPlugin plugin, string methodName)
        {
            typeof(StatsPlusPlugin)
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(plugin, null);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test .\StatsPlus.Tests\StatsPlus.Tests.csproj --filter StatsPlusDiagnosticLoggingTests`

Expected: compile or reflection failure because `GetDiagnosticLogPath` and the new `WriteDiagnosticLog` signature do not exist.

- [ ] **Step 3: Implement log gating and path routing**

In `StatsPlusPlugin.cs`, add:

```csharp
private bool ShouldWriteDiagnosticLog(string gameName)
{
    if (!Settings.EnableDebugLogging)
    {
        return false;
    }

    string settingsKey = GetDebugLoggingSettingsKey(gameName);
    if (string.IsNullOrWhiteSpace(settingsKey))
    {
        return false;
    }

    if (Settings.GameDebugLogging == null)
    {
        Settings.GameDebugLogging = new Dictionary<string, bool>();
    }

    if (!Settings.GameDebugLogging.TryGetValue(settingsKey, out bool isEnabled))
    {
        Settings.GameDebugLogging[settingsKey] = false;
        return false;
    }

    return isEnabled;
}

private string GetDiagnosticLogPath(string gameName)
{
    if (string.IsNullOrWhiteSpace(_diagnosticLogPath))
    {
        return string.Empty;
    }

    string settingsKey = GetDebugLoggingSettingsKey(gameName);
    if (string.IsNullOrWhiteSpace(settingsKey))
    {
        return string.Empty;
    }

    string directory = Path.GetDirectoryName(_diagnosticLogPath);
    string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(_diagnosticLogPath);
    string extension = Path.GetExtension(_diagnosticLogPath);
    return Path.Combine(directory ?? string.Empty, $"{fileNameWithoutExtension}.{settingsKey}{extension}");
}
```

Change `WriteDiagnosticLog` to:

```csharp
private void WriteDiagnosticLog(string gameName, string eventName, string detail)
{
    if (!ShouldWriteDiagnosticLog(gameName))
    {
        return;
    }

    string diagnosticLogPath = GetDiagnosticLogPath(gameName);
    if (string.IsNullOrWhiteSpace(diagnosticLogPath))
    {
        return;
    }

    try
    {
        string directory = Path.GetDirectoryName(diagnosticLogPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string line = string.Format(
            CultureInfo.InvariantCulture,
            "[{0:O}] {1} {2}{3}",
            DateTime.UtcNow,
            eventName,
            detail ?? string.Empty,
            Environment.NewLine);
        File.AppendAllText(diagnosticLogPath, line, Encoding.UTF8);
    }
    catch (Exception ex)
    {
        SimHub.Logging.Current.Warn($"StatsPlus - Failed to write diagnostic log: {ex.Message}");
    }
}
```

Update existing call sites:

```csharp
WriteDiagnosticLog(data.GameName, "STOP FLUSH", ...);
WriteDiagnosticLog(data.GameName, "CONTEXT MISSING", ...);
WriteDiagnosticLog(CurrentGameName, "CONTEXT SWITCH", ...);
WriteDiagnosticLog(data.GameName, "LAP BOUNDARY", ...);
WriteDiagnosticLog(CurrentGameName, "PENDING QUEUED", ...);
WriteDiagnosticLog(gameName, "PENDING SAVED", ...);
WriteDiagnosticLog(data.GameName, "PENDING WAIT", ...);
WriteDiagnosticLog(CurrentGameName, "PENDING CLEARED", ...);
```

For helper code paths that already receive `gameName`, use that parameter. For missing-context code paths, use `data.GameName`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test .\StatsPlus.Tests\StatsPlus.Tests.csproj --filter StatsPlusDiagnosticLoggingTests`

Expected: all `StatsPlusDiagnosticLoggingTests` pass.

- [ ] **Step 5: Run existing lap capture tests as a regression check**

Run: `dotnet test .\StatsPlus.Tests\StatsPlus.Tests.csproj --filter StatsPlusPluginLapCaptureTests`

Expected: all lap capture tests pass, proving diagnostic call-site updates did not break capture flow.

- [ ] **Step 6: Commit**

Run:

```powershell
git add -- 'StatsPlus/StatsPlusPlugin.cs' 'StatsPlus.Tests/StatsPlusDiagnosticLoggingTests.cs'
git commit -m "Gate StatsPlus diagnostics per game"
```

### Task 3: Settings UI Section

**Files:**
- Modify: `StatsPlus/SettingsControl.xaml`
- Modify: `StatsPlus/StatsPlusPlugin.cs`

**Interfaces:**
- Consumes: `StatsPlusPlugin.IsDebugLoggingEnabled : bool`
- Consumes: `StatsPlusPlugin.GameDebugLoggingOptions : ObservableCollection<GameDebugLoggingOption>`

- [ ] **Step 1: Add a compile-verification target**

No separate XAML parser test is needed because this WPF project compiles XAML during `dotnet build`. Use the build as the red/green check for binding names and markup validity.

- [ ] **Step 2: Add the Debug Logging section**

Insert the section below the `Enabled Games` section and before `About`:

```xml
<styles:SHSection Title="Debug Logging">
    <StackPanel Margin="12">
        <Grid Margin="0,0,0,10">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="190" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>

            <TextBlock Grid.Column="0"
                       Margin="0,0,12,0"
                       VerticalAlignment="Center"
                       Text="Debug logging" />
            <CheckBox Grid.Column="1"
                      HorizontalAlignment="Left"
                      Content="Write diagnostic debug log"
                      IsChecked="{Binding DataContext.IsDebugLoggingEnabled, ElementName=Root, Mode=TwoWay}" />
        </Grid>

        <TextBlock Margin="0,0,0,8" TextWrapping="Wrap">
            Diagnostic logs are written to separate files per game. Per-game logging options are available when debug logging is enabled.
        </TextBlock>

        <ItemsControl IsEnabled="{Binding DataContext.IsDebugLoggingEnabled, ElementName=Root}"
                      ItemsSource="{Binding DataContext.GameDebugLoggingOptions, ElementName=Root}">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <CheckBox Margin="0,0,0,6"
                              Content="{Binding DisplayName}"
                              IsChecked="{Binding IsEnabled, Mode=TwoWay}" />
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
</styles:SHSection>
```

Ensure `RefreshGameDebugLoggingOptions()` runs after settings load and reset so this binding has data.

- [ ] **Step 3: Build to verify XAML compiles**

Run: `dotnet build .\StatsPlus.sln /p:SimHubInstallPath=C:\does-not-exist`

Expected: build succeeds.

- [ ] **Step 4: Commit**

Run:

```powershell
git add -- 'StatsPlus/SettingsControl.xaml' 'StatsPlus/StatsPlusPlugin.cs'
git commit -m "Add StatsPlus debug logging settings UI"
```

### Task 4: Full Verification

**Files:**
- Verify all modified files.

**Interfaces:**
- Consumes all interfaces from Tasks 1-3.
- Produces a final verified implementation.

- [ ] **Step 1: Run all tests**

Run: `dotnet test .\StatsPlus.Tests\StatsPlus.Tests.csproj`

Expected: all tests pass.

- [ ] **Step 2: Run solution build**

Run: `dotnet build .\StatsPlus.sln /p:SimHubInstallPath=C:\does-not-exist`

Expected: build succeeds with zero errors.

- [ ] **Step 3: Inspect git diff**

Run: `git diff --check`

Expected: no whitespace errors.

Run: `git status --short`

Expected: only intentional tracked changes are present before final staging or commit.

- [ ] **Step 4: Final commit if Task 3 did not already include all changes**

Run:

```powershell
git add -- 'StatsPlus' 'StatsPlus.Tests'
git commit -m "Add per-game StatsPlus diagnostic logging"
```

Skip this command if there are no uncommitted implementation changes after Tasks 1-3.
