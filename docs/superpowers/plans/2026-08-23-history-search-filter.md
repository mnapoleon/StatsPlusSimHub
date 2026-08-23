# StatsPlus History Search And Filter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add incremental, per-game filtering of stored history by displayed car, circuit, and circuit-layout names.

**Architecture:** Move the now-behavioral `GameHistoryTab` into a focused view-model file that owns its complete summaries, filtered summaries, field selection, query text, and pure matching rule. `StatsPlusPlugin` preserves that state when it rebuilds dynamic tabs and clears lap details when a selected summary becomes hidden; the WPF view binds directly to the tab's filter state.

**Tech Stack:** C# 7+/NET Framework 4.8, WPF/XAML, `ObservableCollection<T>`, `INotifyPropertyChanged`, MSTest; no new packages.

**Spec:** `docs/superpowers/specs/2026-08-23-history-search-filter-design.md`

## Global Constraints

- Search is scoped to the current recorded-game tab; do not add cross-game results.
- Search only the displayed car, circuit name, and circuit layout values.
- Apply case-insensitive substring matching on every keystroke; split whitespace-delimited terms and require every term to match.
- In `All fields`, different terms may match different fields; in a specific field, all terms must match that field.
- Preserve independent in-memory search state by game name across tab switches and history refreshes, but do not persist it in `PluginSettings`.
- Keep the LiteDB schema, repository APIs, telemetry capture, personal-best calculations, and published SimHub properties unchanged.
- Continue targeting `net48` and add no package dependency.
- Use TDD: confirm each focused test fails before adding the production behavior that makes it pass.
- Run the standard no-deploy test and build commands before claiming completion.

---

### Task 1: Add the per-game history search view model

**Files:**
- Create: `StatsPlus/GameHistoryTab.cs`
- Modify: `StatsPlus/LapHistoryModels.cs:83`
- Create: `StatsPlus.Tests/GameHistoryTabSearchTests.cs`

**Interfaces:**
- Consumes: `StoredTrackSummary` display properties from `StatsPlus/LapHistoryModels.cs`.
- Produces: `HistorySearchField`, `HistorySearchFieldOption`, `HistorySummaryMatcher.Matches(StoredTrackSummary, string, HistorySearchField)`, and a behaviorful `GameHistoryTab` exposing `Tracks`, `FilteredTracks`, `SearchText`, `SelectedSearchField`, `SearchFieldOptions`, `HasNoMatchingHistory`, `FilterChanged`, `ContainsVisible`, `RestoreSearchState`, and `ClearSearch`.

- [ ] **Step 1: Write failing matcher tests**

Create `StatsPlus.Tests/GameHistoryTabSearchTests.cs` with focused examples for blank queries, each field, case-insensitive partial terms, cross-field AND matching, and source ordering:

```csharp
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace StatsPlus.Tests
{
    [TestClass]
    public class GameHistoryTabSearchTests
    {
        [TestMethod]
        public void SearchText_PartialTermFiltersImmediatelyAcrossAllFields()
        {
            StoredTrackSummary nordschleife = Summary("Porsche 911", "Nurburgring", "Nordschleife");
            StoredTrackSummary monza = Summary("Porsche 911", "Monza", "Grand Prix");
            GameHistoryTab tab = Tab(nordschleife, monza);

            tab.SearchText = "Nor";
            CollectionAssert.AreEqual(new[] { nordschleife }, tab.FilteredTracks.ToArray());

            tab.SearchText = "Nord";
            CollectionAssert.AreEqual(new[] { nordschleife }, tab.FilteredTracks.ToArray());
        }

        [TestMethod]
        public void SearchText_MultipleTermsCanMatchDifferentFieldsInAllFieldsMode()
        {
            StoredTrackSummary matching = Summary("BMW M4 GT3", "Nurburgring", "Nordschleife");
            StoredTrackSummary wrongCar = Summary("Porsche 911", "Nurburgring", "Nordschleife");
            GameHistoryTab tab = Tab(matching, wrongCar);

            tab.SearchText = "bmw nord";

            CollectionAssert.AreEqual(new[] { matching }, tab.FilteredTracks.ToArray());
        }

        [TestMethod]
        public void SelectedSearchField_RestrictsMatchingToThatDisplayField()
        {
            StoredTrackSummary summary = Summary("Nord Car", "Nurburgring", "Grand Prix");
            GameHistoryTab tab = Tab(summary);
            tab.SearchText = "Nord";

            tab.SelectedSearchField = HistorySearchField.Circuit;
            Assert.AreEqual(0, tab.FilteredTracks.Count);
            Assert.IsTrue(tab.HasNoMatchingHistory);

            tab.SelectedSearchField = HistorySearchField.Car;
            CollectionAssert.AreEqual(new[] { summary }, tab.FilteredTracks.ToArray());
        }

        [TestMethod]
        public void Search_UsesDisplayedValuesAndHandlesNullsCaseInsensitively()
        {
            StoredTrackSummary displayed = Summary(null, "Suzuka Circuit", null);
            GameHistoryTab tab = Tab(displayed);

            tab.SearchText = "ZuKa";

            CollectionAssert.AreEqual(new[] { displayed }, tab.FilteredTracks.ToArray());
        }

        [TestMethod]
        public void BlankSearchAndClear_RestoreAllRowsInSourceOrderAndDefaultField()
        {
            StoredTrackSummary first = Summary("BMW", "Spa", "GP");
            StoredTrackSummary second = Summary("Audi", "Monza", "GP");
            GameHistoryTab tab = Tab(first, second);
            tab.SelectedSearchField = HistorySearchField.Car;
            tab.SearchText = "Audi";

            tab.ClearSearch();

            Assert.AreEqual(string.Empty, tab.SearchText);
            Assert.AreEqual(HistorySearchField.AllFields, tab.SelectedSearchField);
            CollectionAssert.AreEqual(new[] { first, second }, tab.FilteredTracks.ToArray());

            tab.SearchText = "   ";
            CollectionAssert.AreEqual(new[] { first, second }, tab.FilteredTracks.ToArray());
            Assert.IsFalse(tab.HasNoMatchingHistory);
        }

        [TestMethod]
        public void RestoreSearchState_AppliesBothValuesAndRaisesOneFilterChange()
        {
            GameHistoryTab tab = Tab(Summary("BMW", "Spa", "GP"));
            int filterChanges = 0;
            tab.FilterChanged += (sender, args) => filterChanges++;

            tab.RestoreSearchState("spa", HistorySearchField.Circuit);

            Assert.AreEqual("spa", tab.SearchText);
            Assert.AreEqual(HistorySearchField.Circuit, tab.SelectedSearchField);
            Assert.AreEqual(1, filterChanges);
        }

        private static GameHistoryTab Tab(params StoredTrackSummary[] summaries)
        {
            return new GameHistoryTab
            {
                GameName = "Test Game",
                Tracks = new List<StoredTrackSummary>(summaries)
            };
        }

        private static StoredTrackSummary Summary(string car, string circuit, string layout)
        {
            return new StoredTrackSummary
            {
                CarModelDisplay = car,
                CircuitNameDisplay = circuit,
                CircuitLayoutDisplay = layout
            };
        }
    }
}
```

- [ ] **Step 2: Run the new tests and confirm the red state**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter "FullyQualifiedName~GameHistoryTabSearchTests"
```

Expected: FAIL to compile because `HistorySearchField`, `FilteredTracks`, and the search members do not exist.

- [ ] **Step 3: Extract `GameHistoryTab` from the passive lap-model file**

Remove only the existing `GameHistoryTab` declaration from `StatsPlus/LapHistoryModels.cs`; leave `RecordedLap`, `StoredTrackSummary`, `RecordedLapView`, and `StatsPlusSettingsTab` in place.

Create `StatsPlus/GameHistoryTab.cs` and begin with the field types and pure matcher:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace StatsPlus
{
    public enum HistorySearchField
    {
        AllFields,
        Car,
        Circuit,
        Layout
    }

    public sealed class HistorySearchFieldOption
    {
        public HistorySearchFieldOption(HistorySearchField field, string label)
        {
            Field = field;
            Label = label;
        }

        public HistorySearchField Field { get; }
        public string Label { get; }
    }

    internal static class HistorySummaryMatcher
    {
        internal static bool Matches(StoredTrackSummary summary, string searchText, HistorySearchField field)
        {
            if (summary == null)
            {
                return false;
            }

            string[] terms = (searchText ?? string.Empty)
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0)
            {
                return true;
            }

            string[] values = SearchableValues(summary, field);
            return terms.All(term => values.Any(value =>
                (value ?? string.Empty).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static string[] SearchableValues(StoredTrackSummary summary, HistorySearchField field)
        {
            switch (field)
            {
                case HistorySearchField.Car:
                    return new[] { summary.CarModelDisplay };
                case HistorySearchField.Circuit:
                    return new[] { summary.CircuitNameDisplay };
                case HistorySearchField.Layout:
                    return new[] { summary.CircuitLayoutDisplay };
                default:
                    return new[]
                    {
                        summary.CarModelDisplay,
                        summary.CircuitNameDisplay,
                        summary.CircuitLayoutDisplay
                    };
            }
        }
    }
}
```

- [ ] **Step 4: Implement the behaviorful `GameHistoryTab`**

Add this class inside the existing `StatsPlus` namespace in `StatsPlus/GameHistoryTab.cs`, below the matcher and before the namespace's closing brace:

```csharp
public class GameHistoryTab : INotifyPropertyChanged
{
    private static readonly IReadOnlyList<HistorySearchFieldOption> Fields =
        Array.AsReadOnly(new[]
        {
            new HistorySearchFieldOption(HistorySearchField.AllFields, "All fields"),
            new HistorySearchFieldOption(HistorySearchField.Car, "Car"),
            new HistorySearchFieldOption(HistorySearchField.Circuit, "Circuit"),
            new HistorySearchFieldOption(HistorySearchField.Layout, "Layout")
        });

    private List<StoredTrackSummary> _tracks = new List<StoredTrackSummary>();
    private string _searchText = string.Empty;
    private HistorySearchField _selectedSearchField = HistorySearchField.AllFields;

    public event PropertyChangedEventHandler PropertyChanged;
    public event EventHandler FilterChanged;

    public string Header => GameName;
    public string GameName { get; set; } = string.Empty;

    public List<StoredTrackSummary> Tracks
    {
        get => _tracks;
        set
        {
            _tracks = value ?? new List<StoredTrackSummary>();
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public ObservableCollection<StoredTrackSummary> FilteredTracks { get; } =
        new ObservableCollection<StoredTrackSummary>();

    public IReadOnlyList<HistorySearchFieldOption> SearchFieldOptions => Fields;

    public string SearchText
    {
        get => _searchText;
        set
        {
            string next = value ?? string.Empty;
            if (string.Equals(_searchText, next, StringComparison.Ordinal))
            {
                return;
            }

            _searchText = next;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public HistorySearchField SelectedSearchField
    {
        get => _selectedSearchField;
        set
        {
            if (_selectedSearchField == value)
            {
                return;
            }

            _selectedSearchField = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public bool HasNoMatchingHistory =>
        _tracks.Count > 0 &&
        !string.IsNullOrWhiteSpace(_searchText) &&
        FilteredTracks.Count == 0;

    public bool ContainsVisible(StoredTrackSummary summary)
    {
        return summary != null && FilteredTracks.Contains(summary);
    }

    public void RestoreSearchState(string searchText, HistorySearchField field)
    {
        _searchText = searchText ?? string.Empty;
        _selectedSearchField = field;
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(SelectedSearchField));
        ApplyFilter();
    }

    public void ClearSearch()
    {
        RestoreSearchState(string.Empty, HistorySearchField.AllFields);
    }

    public override string ToString()
    {
        return GameName;
    }

    private void ApplyFilter()
    {
        List<StoredTrackSummary> matches = _tracks
            .Where(summary => HistorySummaryMatcher.Matches(summary, _searchText, _selectedSearchField))
            .ToList();

        FilteredTracks.Clear();
        foreach (StoredTrackSummary summary in matches)
        {
            FilteredTracks.Add(summary);
        }

        OnPropertyChanged(nameof(HasNoMatchingHistory));
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
```

- [ ] **Step 5: Run the focused tests and confirm green**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter "FullyQualifiedName~GameHistoryTabSearchTests"
```

Expected: PASS for all `GameHistoryTabSearchTests` tests.

- [ ] **Step 6: Run the existing tab-layout tests to catch extraction regressions**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter "FullyQualifiedName~StatsPlusPluginTabLayoutTests"
```

Expected: PASS; existing `tab.Tracks` consumers continue to work.

- [ ] **Step 7: Commit the search view model**

```powershell
git add -- StatsPlus/GameHistoryTab.cs StatsPlus/LapHistoryModels.cs StatsPlus.Tests/GameHistoryTabSearchTests.cs
git commit -m "feat: add game history search model"
```

---

### Task 2: Preserve filters and clear hidden lap selections

**Files:**
- Modify: `StatsPlus/StatsPlusPlugin.cs:792`
- Modify: `StatsPlus.Tests/StatsPlusPluginTabLayoutTests.cs:64`

**Interfaces:**
- Consumes: Task 1's `GameHistoryTab.RestoreSearchState`, `FilterChanged`, `FilteredTracks`, and `ContainsVisible`.
- Produces: `StatsPlusPlugin.GameHistoryTab_FilterChanged(object, EventArgs)` plus refresh behavior that copies search state by case-insensitive game name and restores selections only from visible rows.

- [ ] **Step 1: Add failing plugin workflow tests**

Add these tests to `StatsPlus.Tests/StatsPlusPluginTabLayoutTests.cs`:

```csharp
[TestMethod]
public void GameTabs_KeepIndependentSearchStateAcrossTabSwitches()
{
    SeedLap("iRacing", "Mazda MX-5", "Laguna Seca", "Laguna Seca", 91.25,
        new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc));
    SeedLap("LMU", "Ferrari 499P", "Le Mans", "Le Mans - 24h", 210.5,
        new DateTime(2026, 8, 23, 13, 0, 0, DateTimeKind.Utc));
    StatsPlusPlugin plugin = CreateInitializedPlugin();
    GameHistoryTab iracing = plugin.GameHistoryTabs.Single(tab => tab.GameName == "iRacing");
    GameHistoryTab lmu = plugin.GameHistoryTabs.Single(tab => tab.GameName == "LMU");

    iracing.RestoreSearchState("maz", HistorySearchField.Car);
    plugin.SelectedTopLevelTab = lmu;
    lmu.RestoreSearchState("mans", HistorySearchField.Circuit);
    plugin.SelectedTopLevelTab = iracing;

    Assert.AreEqual("maz", iracing.SearchText);
    Assert.AreEqual(HistorySearchField.Car, iracing.SelectedSearchField);
    Assert.AreEqual("mans", lmu.SearchText);
    Assert.AreEqual(HistorySearchField.Circuit, lmu.SelectedSearchField);
}

[TestMethod]
public void RefreshStoredTrackSummaries_PreservesSearchStateByGameName()
{
    SeedLap("iRacing", "Mazda MX-5", "Laguna Seca", "Laguna Seca", 91.25,
        new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc));
    SeedLap("iRacing", "BMW M4", "Nurburgring", "Nurburgring - Nordschleife", 420.0,
        new DateTime(2026, 8, 23, 13, 0, 0, DateTimeKind.Utc));
    StatsPlusPlugin plugin = CreateInitializedPlugin();
    GameHistoryTab original = plugin.GameHistoryTabs.Single();
    original.RestoreSearchState("nord", HistorySearchField.Layout);

    plugin.RefreshStoredTrackSummaries();

    GameHistoryTab refreshed = plugin.GameHistoryTabs.Single();
    Assert.AreNotSame(original, refreshed);
    Assert.AreEqual("nord", refreshed.SearchText);
    Assert.AreEqual(HistorySearchField.Layout, refreshed.SelectedSearchField);
    Assert.AreEqual(1, refreshed.FilteredTracks.Count);
    Assert.AreEqual("Nurburgring - Nordschleife", refreshed.FilteredTracks[0].TrackNameWithConfig);
}

[TestMethod]
public void FilteringOutSelectedSummary_ClearsRecordedLapDetails()
{
    SeedLap("iRacing", "Mazda MX-5", "Laguna Seca", "Laguna Seca", 91.25,
        new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc));
    SeedLap("iRacing", "BMW M4", "Nurburgring", "Nurburgring - Nordschleife", 420.0,
        new DateTime(2026, 8, 23, 13, 0, 0, DateTimeKind.Utc));
    StatsPlusPlugin plugin = CreateInitializedPlugin();
    GameHistoryTab tab = plugin.GameHistoryTabs.Single();
    plugin.SelectedTrackSummary = tab.Tracks.Single(summary => summary.CarModel == "Mazda MX-5");
    plugin.SelectedLap = plugin.SelectedTrackLaps.Single();

    tab.SearchText = "BMW";

    Assert.IsNull(plugin.SelectedTrackSummary);
    Assert.IsNull(plugin.SelectedLap);
    Assert.AreEqual(0, plugin.SelectedTrackLaps.Count);
    Assert.AreEqual("Select a track row above to inspect recorded laps.", plugin.SelectedTrackCaption);
}

[TestMethod]
public void RefreshStoredTrackSummaries_DoesNotRestoreASelectionHiddenByPreservedFilter()
{
    SeedLap("iRacing", "Mazda MX-5", "Laguna Seca", "Laguna Seca", 91.25,
        new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc));
    SeedLap("iRacing", "BMW M4", "Nurburgring", "Nurburgring - Nordschleife", 420.0,
        new DateTime(2026, 8, 23, 13, 0, 0, DateTimeKind.Utc));
    StatsPlusPlugin plugin = CreateInitializedPlugin();
    GameHistoryTab tab = plugin.GameHistoryTabs.Single();
    tab.RestoreSearchState("Mazda", HistorySearchField.Car);
    plugin.SelectedTrackSummary = tab.Tracks.Single(summary => summary.CarModel == "BMW M4");

    plugin.RefreshStoredTrackSummaries();

    Assert.IsNull(plugin.SelectedTrackSummary);
    Assert.AreEqual(0, plugin.SelectedTrackLaps.Count);
}
```

- [ ] **Step 2: Run the new workflow tests and confirm the red state**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter "FullyQualifiedName~GameTabs_KeepIndependentSearchStateAcrossTabSwitches|FullyQualifiedName~RefreshStoredTrackSummaries_PreservesSearchStateByGameName|FullyQualifiedName~FilteringOutSelectedSummary_ClearsRecordedLapDetails|FullyQualifiedName~RefreshStoredTrackSummaries_DoesNotRestoreASelectionHiddenByPreservedFilter"
```

Expected: the independent-state test passes from Task 1, while refresh preservation and hidden-selection tests FAIL because the plugin neither copies search state nor observes filter changes.

- [ ] **Step 3: Preserve search state while rebuilding game tabs**

In the `Apply()` local function inside `StatsPlusPlugin.RefreshStoredTrackSummaries`, capture old tabs by game name before clearing the collections, restore their state onto replacements, and subscribe to filter changes:

```csharp
Dictionary<string, GameHistoryTab> previousTabs = GameHistoryTabs
    .ToDictionary(tab => tab.GameName, StringComparer.OrdinalIgnoreCase);

GameHistoryTabs.Clear();
TopLevelTabs.Clear();
foreach (GameHistoryTab tab in tabs)
{
    if (previousTabs.TryGetValue(tab.GameName, out GameHistoryTab previousTab))
    {
        tab.RestoreSearchState(previousTab.SearchText, previousTab.SelectedSearchField);
    }

    tab.FilterChanged += GameHistoryTab_FilterChanged;
    GameHistoryTabs.Add(tab);
    TopLevelTabs.Add(tab);
}
```

Keep the existing Settings-tab append and top-level-tab selection resolution. Replace the final selection restoration over the unfiltered `summaries` list with restoration through the matching tab's visible rows:

```csharp
GameHistoryTab selectedSummaryTab = tabs.FirstOrDefault(tab =>
    string.Equals(tab.GameName, selectedGame, StringComparison.OrdinalIgnoreCase));
SelectedTrackSummary = selectedSummaryTab?.FilteredTracks.FirstOrDefault(summary =>
    string.Equals(summary.CarModel, selectedCar, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(summary.TrackNameWithConfig, selectedTrackConfig, StringComparison.OrdinalIgnoreCase));
```

- [ ] **Step 4: Clear details when a live filter hides the selection**

Add this private handler near `LoadSelectedTrackLaps` in `StatsPlus/StatsPlusPlugin.cs`:

```csharp
private void GameHistoryTab_FilterChanged(object sender, EventArgs e)
{
    GameHistoryTab tab = sender as GameHistoryTab;
    if (tab == null || SelectedTrackSummary == null)
    {
        return;
    }

    if (!string.Equals(tab.GameName, SelectedTrackSummary.GameName, StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    if (!tab.ContainsVisible(SelectedTrackSummary))
    {
        SelectedTrackSummary = null;
    }
}
```

Rely on the existing `SelectedTrackSummary` setter and `LoadSelectedTrackLaps()` to clear `SelectedTrackLaps` and `SelectedLap`; do not duplicate that logic in the event handler.

- [ ] **Step 5: Run the focused workflow tests and confirm green**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter "FullyQualifiedName~StatsPlusPluginTabLayoutTests"
```

Expected: PASS for the new filter-state tests and every existing dynamic-tab/selection test.

- [ ] **Step 6: Commit plugin integration**

```powershell
git add -- StatsPlus/StatsPlusPlugin.cs StatsPlus.Tests/StatsPlusPluginTabLayoutTests.cs
git commit -m "feat: preserve per-game history filters"
```

---

### Task 3: Add the WPF search controls and complete verification

**Files:**
- Modify: `StatsPlus/SettingsControl.xaml:82`
- Modify: `StatsPlus/SettingsControl.xaml.cs:26`
- Modify: `StatsPlus.Tests/SettingsControlXamlTests.cs:11`

**Interfaces:**
- Consumes: Task 1's `SearchFieldOptions`, `SelectedSearchField`, `SearchText`, `FilteredTracks`, `HasNoMatchingHistory`, and `ClearSearch`.
- Produces: `SettingsControl.ClearHistorySearchButton_Click(object, RoutedEventArgs)` and bindings that update the filter on every keystroke.

- [ ] **Step 1: Add failing XAML contract tests**

Add this method to `StatsPlus.Tests/SettingsControlXamlTests.cs`:

```csharp
[TestMethod]
public void StoredHistoryGrid_HasIncrementalFieldScopedSearchControls()
{
    string xaml = File.ReadAllText(FindSettingsControlXamlPath());

    StringAssert.Contains(xaml, "ItemsSource=\"{Binding SearchFieldOptions}\"");
    StringAssert.Contains(xaml, "DisplayMemberPath=\"Label\"");
    StringAssert.Contains(xaml, "SelectedValuePath=\"Field\"");
    StringAssert.Contains(xaml, "SelectedValue=\"{Binding SelectedSearchField, Mode=TwoWay}\"");
    StringAssert.Contains(xaml, "Text=\"{Binding SearchText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"");
    StringAssert.Contains(xaml, "Click=\"ClearHistorySearchButton_Click\"");
    StringAssert.Contains(xaml, "ItemsSource=\"{Binding FilteredTracks}\"");
    StringAssert.Contains(xaml, "Text=\"No matching history.\"");
    StringAssert.Contains(xaml, "Visibility=\"{Binding HasNoMatchingHistory, Converter={StaticResource BooleanToVisibilityConverter}}\"");
    Assert.IsFalse(xaml.Contains("ItemsSource=\"{Binding Tracks}\""));
}
```

- [ ] **Step 2: Run the XAML test and confirm the red state**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter "FullyQualifiedName~StoredHistoryGrid_HasIncrementalFieldScopedSearchControls"
```

Expected: FAIL because the search controls and filtered binding are absent.

- [ ] **Step 3: Add the selector, incremental text box, Clear action, and no-results state**

In the Stored History `StackPanel` in `StatsPlus/SettingsControl.xaml`, insert this block between the existing action toolbar and the summary `DataGrid`:

```xml
<Grid Margin="0,0,0,10">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto" />
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>

    <ComboBox Grid.Column="0"
              Width="125"
              ItemsSource="{Binding SearchFieldOptions}"
              DisplayMemberPath="Label"
              SelectedValuePath="Field"
              SelectedValue="{Binding SelectedSearchField, Mode=TwoWay}" />
    <TextBox Grid.Column="1"
             MinWidth="220"
             Margin="8,0"
             ToolTip="Search car, circuit, or layout"
             Text="{Binding SearchText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
    <Button Grid.Column="2"
            Width="70"
            Click="ClearHistorySearchButton_Click"
            Content="Clear" />
</Grid>

<TextBlock Margin="0,0,0,8"
           Foreground="#D8D8D8"
           FontStyle="Italic"
           Text="No matching history."
           Visibility="{Binding HasNoMatchingHistory, Converter={StaticResource BooleanToVisibilityConverter}}" />
```

Change only the summary grid's source from:

```xml
ItemsSource="{Binding Tracks}"
```

to:

```xml
ItemsSource="{Binding FilteredTracks}"
```

Keep all existing columns, selection binding, sizes, and history actions unchanged.

- [ ] **Step 4: Wire the Clear button through the existing code-behind pattern**

Add this handler to `StatsPlus/SettingsControl.xaml.cs` near `RefreshHistoryButton_Click`:

```csharp
private void ClearHistorySearchButton_Click(object sender, RoutedEventArgs e)
{
    if ((sender as FrameworkElement)?.DataContext is GameHistoryTab gameTab)
    {
        gameTab.ClearSearch();
    }
}
```

- [ ] **Step 5: Run the focused model, plugin, and XAML tests**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj --filter "FullyQualifiedName~GameHistoryTabSearchTests|FullyQualifiedName~StatsPlusPluginTabLayoutTests|FullyQualifiedName~SettingsControlXamlTests"
```

Expected: PASS for all new and existing history UI tests.

- [ ] **Step 6: Run the full automated test suite**

Run:

```powershell
dotnet test StatsPlus.Tests\StatsPlus.Tests.csproj
```

Expected: PASS. The known LiteDB `4.1.4` NU1904 warning may remain; there must be no test failures.

- [ ] **Step 7: Build the plugin without deploying to SimHub**

Run:

```powershell
dotnet build StatsPlus\StatsPlus.csproj /p:SimHubInstallPath=C:\does-not-exist
```

Expected: build succeeds without copying plugin files into the SimHub installation. The known LiteDB `4.1.4` NU1904 warning may remain.

- [ ] **Step 8: Inspect the final diff for unintended storage or telemetry changes**

Run:

```powershell
git diff --check
git diff --stat
git status --short
```

Expected: only the uncommitted WPF UI and XAML-test files from Task 3 are present. The model, plugin integration, spec, and plan are already committed; no LiteDB document, repository, telemetry-capture, or settings changes appear.

- [ ] **Step 9: Commit the WPF feature and tests**

```powershell
git add -- StatsPlus/SettingsControl.xaml StatsPlus/SettingsControl.xaml.cs StatsPlus.Tests/SettingsControlXamlTests.cs
git commit -m "feat: add history search controls"
```

- [ ] **Step 10: Record optional manual SimHub smoke-test evidence**

If the user requests deployment after automated verification, build with the real install path and confirm in SimHub that typing `Nord` immediately narrows the current game tab, changing the selector scopes the match, switching tabs preserves each query, Clear restores all rows, and a hidden selected row clears Recorded Laps. Do not deploy as part of this plan unless explicitly requested.
