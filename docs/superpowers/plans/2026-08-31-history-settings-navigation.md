# StatsPlus History And Settings Navigation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace dynamic top-level game tabs with stable `History` and `Settings` tabs while preserving per-game history browsing inside History.

**Architecture:** Add a small `StatsPlusHistoryTab` view model beside `StatsPlusSettingsTab`. Keep `GameHistoryTabs` as the nested game collection, and change top-level selection logic so `SelectedGameHistoryTab` is independent from `SelectedTopLevelTab`.

**Tech Stack:** C#/.NET Framework 4.8, WPF XAML, SimHub `SHTabControl`, MSTest.

**Spec:** `docs/superpowers/specs/2026-08-31-history-settings-navigation-design.md`

## Global Constraints

- Keep changes within the existing WPF/SimHub stack without new dependencies.
- Do not change LiteDB schema or lap persistence.
- Do not change lap capture, personal best calculation, published SimHub property names, or diagnostic log content.
- Preserve the new row-based Data Management section.
- Standard verification commands are `dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj` and `dotnet build StatsPlus\StatsPlus.csproj /p:SimHubInstallPath=C:\does-not-exist`.

---

### Task 1: Stable Top-Level Navigation Model

**Files:**
- Modify: `StatsPlus/LapHistoryModels.cs`
- Modify: `StatsPlus/StatsPlusPlugin.cs`
- Test: `StatsPlus.Tests/StatsPlusPluginTabLayoutTests.cs`

**Interfaces:**
- Produces: `public class StatsPlusHistoryTab { public string Header => "History"; }`
- Produces: `StatsPlusPlugin.TopLevelTabs` containing history tab and settings tab.
- Produces: `StatsPlusPlugin.SelectedGameHistoryTab` that does not clear when `SelectedTopLevelTab` is Settings.

- [ ] **Step 1: Write failing tests for stable top-level tabs**

Add or update tests in `StatsPlus.Tests/StatsPlusPluginTabLayoutTests.cs`:

```csharp
[TestMethod]
public void Init_WithNoHistory_CreatesHistoryAndSettingsTopLevelTabs()
{
    StatsPlusPlugin plugin = CreateInitializedPlugin();

    CollectionAssert.AreEqual(
        new[] { "History", "Settings" },
        TabHeaders(plugin));
    Assert.IsInstanceOfType(plugin.TopLevelTabs[0], typeof(StatsPlusHistoryTab));
    Assert.IsInstanceOfType(plugin.TopLevelTabs[1], typeof(StatsPlusSettingsTab));
    Assert.AreSame(plugin.TopLevelTabs[0], plugin.SelectedTopLevelTab);
    Assert.IsNull(plugin.SelectedGameHistoryTab);
}

[TestMethod]
public void Init_WithHistory_KeepsGamesNestedUnderHistory()
{
    SeedLap("iRacing", "Mazda MX-5", "Laguna Seca", "Laguna Seca", 91.25, new DateTime(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc));
    SeedLap("LMU", "Ferrari 499P", "Le Mans", "Le Mans - 24h", 210.5, new DateTime(2026, 7, 31, 13, 0, 0, DateTimeKind.Utc));

    StatsPlusPlugin plugin = CreateInitializedPlugin();

    CollectionAssert.AreEqual(
        new[] { "History", "Settings" },
        TabHeaders(plugin));
    CollectionAssert.AreEqual(
        new[] { "iRacing", "LMU" },
        plugin.GameHistoryTabs.Select(tab => tab.Header).ToArray());
    Assert.IsInstanceOfType(plugin.SelectedTopLevelTab, typeof(StatsPlusHistoryTab));
    Assert.AreEqual("iRacing", plugin.SelectedGameHistoryTab.GameName);
}

[TestMethod]
public void SelectingSettings_DoesNotClearSelectedGameHistoryTab()
{
    SeedLap("iRacing", "Mazda MX-5", "Laguna Seca", "Laguna Seca", 91.25, new DateTime(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc));
    StatsPlusPlugin plugin = CreateInitializedPlugin();

    plugin.SelectedTopLevelTab = plugin.TopLevelTabs.OfType<StatsPlusSettingsTab>().Single();

    Assert.IsInstanceOfType(plugin.SelectedTopLevelTab, typeof(StatsPlusSettingsTab));
    Assert.IsNotNull(plugin.SelectedGameHistoryTab);
    Assert.AreEqual("iRacing", plugin.SelectedGameHistoryTab.GameName);
}
```

- [ ] **Step 2: Run tests to verify red**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter StatsPlusPluginTabLayoutTests
```

Expected: new/updated tests fail because top-level tabs still contain dynamic game tabs and no `StatsPlusHistoryTab` exists.

- [ ] **Step 3: Implement minimal model**

In `StatsPlus/LapHistoryModels.cs`, add:

```csharp
public class StatsPlusHistoryTab
{
    public string Header => "History";
}
```

In `StatsPlus/StatsPlusPlugin.cs`, add:

```csharp
private readonly StatsPlusHistoryTab _historyTab = new StatsPlusHistoryTab();
```

Update `RefreshStoredTrackSummaries()` so `TopLevelTabs` is populated once with `_historyTab` and `_settingsTab`, while game tabs are only added to `GameHistoryTabs`. After refreshing game tabs, keep `SelectedGameHistoryTab` by name when possible, otherwise choose the first game tab or null.

Update `SelectedTopLevelTab` setter so it only updates the top-level selection and does not set `SelectedGameHistoryTab` to null when Settings is selected.

Update `ResolveSelectedTopLevelTab(...)` to return `_historyTab` or `_settingsTab`, preserving Settings when it was previously selected.

- [ ] **Step 4: Run tests to verify green**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter StatsPlusPluginTabLayoutTests
```

Expected: tab layout tests pass after updating old expectations to the stable top-level model.

---

### Task 2: Nested History XAML

**Files:**
- Modify: `StatsPlus/SettingsControl.xaml`
- Test: `StatsPlus.Tests/SettingsControlXamlTests.cs`

**Interfaces:**
- Consumes: `StatsPlusHistoryTab`
- Consumes: `StatsPlusPlugin.GameHistoryTabs`
- Consumes: `StatsPlusPlugin.SelectedGameHistoryTab`
- Produces: History template with nested `SHTabControl` bound to game tabs.

- [ ] **Step 1: Write failing XAML tests**

Add tests in `StatsPlus.Tests/SettingsControlXamlTests.cs`:

```csharp
[TestMethod]
public void HistoryTemplate_ContainsNestedGameTabs()
{
    string xaml = File.ReadAllText(FindSettingsControlXamlPath());
    string historyTemplate = ExtractTemplate(xaml, "local:StatsPlusHistoryTab");

    StringAssert.Contains(historyTemplate, "ItemsSource=\"{Binding DataContext.GameHistoryTabs, ElementName=Root}\"");
    StringAssert.Contains(historyTemplate, "SelectedItem=\"{Binding DataContext.SelectedGameHistoryTab, ElementName=Root, Mode=TwoWay}\"");
    StringAssert.Contains(historyTemplate, "DataType=\"{x:Type local:GameHistoryTab}\"");
}

[TestMethod]
public void RootTabs_AreStableTopLevelTabs()
{
    string xaml = File.ReadAllText(FindSettingsControlXamlPath());

    StringAssert.Contains(xaml, "ItemsSource=\"{Binding TopLevelTabs}\"");
    Assert.IsFalse(xaml.Contains("SelectedItem=\"{Binding SelectedTopLevelTab, Mode=TwoWay}\">\r\n                <styles:SHTabControl.ItemTemplate>\r\n                    <DataTemplate>\r\n                        <TextBlock Text=\"{Binding Header}\" />\r\n                    </DataTemplate>\r\n                </styles:SHTabControl.ItemTemplate>\r\n\r\n                <styles:SHTabControl.Resources>\r\n                    <DataTemplate DataType=\"{x:Type local:GameHistoryTab}\">"));
}
```

- [ ] **Step 2: Run tests to verify red**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter SettingsControlXamlTests
```

Expected: tests fail because the root tab control still directly templates `GameHistoryTab`.

- [ ] **Step 3: Move game template under History**

In `StatsPlus/SettingsControl.xaml`, keep the root `SHTabControl` bound to `TopLevelTabs`. Replace the root-level `GameHistoryTab` template with a `StatsPlusHistoryTab` template. Inside that template, add a nested `styles:SHTabControl`:

```xml
<DataTemplate DataType="{x:Type local:StatsPlusHistoryTab}">
    <StackPanel>
        <TextBlock Margin="12"
                   FontStyle="Italic"
                   Text="No stored history."
                   Visibility="{Binding DataContext.HasNoStoredHistory, ElementName=Root, Converter={StaticResource BooleanToVisibilityConverter}}" />
        <styles:SHTabControl ItemsSource="{Binding DataContext.GameHistoryTabs, ElementName=Root}"
                             SelectedItem="{Binding DataContext.SelectedGameHistoryTab, ElementName=Root, Mode=TwoWay}">
            <styles:SHTabControl.ItemTemplate>
                <DataTemplate>
                    <TextBlock Text="{Binding Header}" />
                </DataTemplate>
            </styles:SHTabControl.ItemTemplate>
            <styles:SHTabControl.Resources>
                <DataTemplate DataType="{x:Type local:GameHistoryTab}">
                    <!-- move the existing Stored History and Recorded Laps sections here unchanged -->
                </DataTemplate>
            </styles:SHTabControl.Resources>
        </styles:SHTabControl>
    </StackPanel>
</DataTemplate>
```

Expose `HasNoStoredHistory => GameHistoryTabs.Count == 0` from `StatsPlusPlugin` and raise `OnPropertyChanged(nameof(HasNoStoredHistory))` whenever `GameHistoryTabs` is refreshed.

- [ ] **Step 4: Run tests to verify green**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter SettingsControlXamlTests
```

Expected: XAML tests pass.

---

### Task 3: Selection And Clear Data Regression

**Files:**
- Modify: `StatsPlus/StatsPlusPlugin.cs`
- Test: `StatsPlus.Tests/StatsPlusPluginTabLayoutTests.cs`

**Interfaces:**
- Consumes: `ClearGameData(string gameName)`
- Produces: stable top-level selection after per-game deletion.

- [ ] **Step 1: Add or update failing selection tests**

Update `ClearGameData_RemovesNamedGameWithoutRequiringSelectedGameTab` to assert:

```csharp
Assert.IsInstanceOfType(plugin.SelectedTopLevelTab, typeof(StatsPlusSettingsTab));
Assert.IsNotNull(plugin.GameHistoryTabs.SingleOrDefault(tab => tab.GameName == "iRacing"));
Assert.IsNull(plugin.GameHistoryTabs.SingleOrDefault(tab => tab.GameName == "LMU"));
```

Add:

```csharp
[TestMethod]
public void ClearSelectedGameData_RemovesSelectedNestedGameAndPreservesHistoryTopLevelTab()
{
    SeedLap("iRacing", "Mazda MX-5", "Laguna Seca", "Laguna Seca", 91.25, new DateTime(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc));
    SeedLap("LMU", "Ferrari 499P", "Le Mans", "Le Mans - 24h", 210.5, new DateTime(2026, 7, 31, 13, 0, 0, DateTimeKind.Utc));
    StatsPlusPlugin plugin = CreateInitializedPlugin();
    plugin.SelectedGameHistoryTab = plugin.GameHistoryTabs.Single(tab => tab.GameName == "LMU");

    plugin.ClearSelectedGameData();

    Assert.IsInstanceOfType(plugin.SelectedTopLevelTab, typeof(StatsPlusHistoryTab));
    Assert.AreEqual("iRacing", plugin.SelectedGameHistoryTab.GameName);
}
```

- [ ] **Step 2: Run tests to verify red**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter StatsPlusPluginTabLayoutTests
```

Expected: tests fail until clear/refresh selection logic uses nested game selection.

- [ ] **Step 3: Implement selection refresh logic**

In `RefreshStoredTrackSummaries()`, capture `previousSelectedGameName = SelectedGameHistoryTab?.GameName` before clearing the game tabs. After rebuilding `GameHistoryTabs`, set:

```csharp
SelectedGameHistoryTab = tabs.FirstOrDefault(tab =>
    string.Equals(tab.GameName, previousSelectedGameName, StringComparison.OrdinalIgnoreCase))
    ?? tabs.FirstOrDefault();
```

When clearing a game, if the cleared game was selected, let the refresh choose the next available nested game. Do not force top-level selection away from Settings.

- [ ] **Step 4: Run tests to verify green**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter StatsPlusPluginTabLayoutTests
```

Expected: tab selection and clearing tests pass.

---

### Task 4: Full Verification

**Files:**
- Test/build only.

**Interfaces:**
- Consumes: all previous tasks.
- Produces: verified working tree.

- [ ] **Step 1: Run focused UI/navigation tests**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter "SettingsControlXamlTests|StatsPlusPluginTabLayoutTests|StatsPlusDebugLoggingSettingsTests"
```

Expected: all focused tests pass.

- [ ] **Step 2: Run full test suite**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj
```

Expected: all tests pass. The known `LiteDB 4.1.4` `NU1904` warning may appear.

- [ ] **Step 3: Run no-deploy build**

Run:

```powershell
dotnet build StatsPlus\StatsPlus.csproj /p:SimHubInstallPath=C:\does-not-exist
```

Expected: build succeeds. The known `LiteDB 4.1.4` `NU1904` warning may appear.

- [ ] **Step 4: Review diff**

Run:

```powershell
git diff -- StatsPlus/LapHistoryModels.cs StatsPlus/StatsPlusPlugin.cs StatsPlus/SettingsControl.xaml StatsPlus.Tests/SettingsControlXamlTests.cs StatsPlus.Tests/StatsPlusPluginTabLayoutTests.cs
```

Expected: diff contains only navigation/layout/test changes plus already-approved UI work in the same touched files.
