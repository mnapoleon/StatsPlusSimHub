using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace StatsPlus.Tests
{
    [TestClass]
    public class SettingsControlXamlTests
    {
        [TestMethod]
        public void StoredHistoryGrid_UsesCircuitNameAndLayoutColumns()
        {
            string xaml = File.ReadAllText(FindSettingsControlXamlPath());

            StringAssert.Contains(xaml, "Header=\"Circuit Name\" Binding=\"{Binding CircuitNameDisplay}\"");
            StringAssert.Contains(xaml, "Header=\"Circuit Layout\" Binding=\"{Binding CircuitLayoutDisplay}\"");
            Assert.IsFalse(xaml.Contains("Header=\"Track\" Binding=\"{Binding TrackName}\""));
            Assert.IsFalse(xaml.Contains("Header=\"Variation\" Binding=\"{Binding TrackNameWithConfigDisplay}\""));
        }

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

        [TestMethod]
        public void DestructiveHistoryActions_AreShownAsPerGameRowsInSettings()
        {
            string xaml = File.ReadAllText(FindSettingsControlXamlPath());
            string historyTemplate = ExtractTemplate(xaml, "local:GameHistoryTab");
            string settingsTemplate = ExtractTemplate(xaml, "local:StatsPlusSettingsTab");

            Assert.IsFalse(historyTemplate.Contains("Click=\"ClearSelectedGameButton_Click\""));
            Assert.IsFalse(historyTemplate.Contains("Click=\"ClearAllDataButton_Click\""));
            StringAssert.Contains(settingsTemplate, "Title=\"Data Management\"");
            StringAssert.Contains(settingsTemplate, "ItemsSource=\"{Binding DataContext.GameHistoryTabs, ElementName=Root}\"");
            Assert.IsFalse(settingsTemplate.Contains("SelectedItem=\"{Binding DataContext.SelectedGameHistoryTab, ElementName=Root, Mode=TwoWay}\""));
            StringAssert.Contains(settingsTemplate, "CommandParameter=\"{Binding GameName}\"");
            StringAssert.Contains(settingsTemplate, "Click=\"ClearGameDataButton_Click\"");
            StringAssert.Contains(settingsTemplate, "Click=\"ClearAllDataButton_Click\"");
        }

        [TestMethod]
        public void SettingsTemplate_CombinesGameRecordingAndDebugLoggingOptions()
        {
            string xaml = File.ReadAllText(FindSettingsControlXamlPath());
            string settingsTemplate = ExtractTemplate(xaml, "local:StatsPlusSettingsTab");

            StringAssert.Contains(settingsTemplate, "Title=\"Game Options\"");
            Assert.IsFalse(settingsTemplate.Contains("Title=\"Enabled Games\""));
            Assert.IsFalse(settingsTemplate.Contains("Title=\"Debug Logging\""));
            StringAssert.Contains(settingsTemplate, "Text=\"Record laps\"");
            StringAssert.Contains(settingsTemplate, "Text=\"Debug logs\"");
            StringAssert.Contains(settingsTemplate, "ItemsSource=\"{Binding DataContext.GameOptions, ElementName=Root}\"");
            StringAssert.Contains(settingsTemplate, "IsChecked=\"{Binding IsRecordingEnabled, Mode=TwoWay}\"");
            StringAssert.Contains(settingsTemplate, "IsEnabled=\"{Binding DataContext.IsDebugLoggingEnabled, ElementName=Root}\"");
            StringAssert.Contains(settingsTemplate, "IsChecked=\"{Binding IsDebugLoggingEnabled, Mode=TwoWay}\"");
        }

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
        public void RootTemplates_AreStableHistoryAndSettingsTabs()
        {
            string xaml = File.ReadAllText(FindSettingsControlXamlPath());

            StringAssert.Contains(xaml, "ItemsSource=\"{Binding TopLevelTabs}\"");
            StringAssert.Contains(xaml, "DataType=\"{x:Type local:StatsPlusHistoryTab}\"");
            StringAssert.Contains(xaml, "DataType=\"{x:Type local:StatsPlusSettingsTab}\"");
        }

        [TestMethod]
        public void RecordedLapsGrid_UsesEditableValidityCheckboxWithoutToggleButton()
        {
            string xaml = File.ReadAllText(FindSettingsControlXamlPath());
            string historyTemplate = ExtractTemplate(xaml, "local:GameHistoryTab");
            string recordedLapsSection = ExtractSection(historyTemplate, "Recorded Laps");
            string recordedLapsGrid = ExtractElementStart(recordedLapsSection, "DataGrid");

            Assert.IsFalse(recordedLapsSection.Contains("Toggle selected lap valid"));
            Assert.IsFalse(recordedLapsSection.Contains("ToggleLapValidityButton_Click"));
            Assert.IsFalse(recordedLapsGrid.Contains("IsReadOnly=\"True\""));
            StringAssert.Contains(recordedLapsSection, "<DataGridTemplateColumn Header=\"Valid\" Width=\"60\">");
            StringAssert.Contains(recordedLapsSection, "<CheckBox");
            StringAssert.Contains(recordedLapsSection, "IsChecked=\"{Binding IsValid, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"");
            StringAssert.Contains(recordedLapsSection, "Click=\"LapValidityCheckBox_Click\"");
            StringAssert.Contains(recordedLapsSection, "IsReadOnly=\"True\" Binding=\"{Binding LapNumber}\"");
            StringAssert.Contains(recordedLapsSection, "IsReadOnly=\"True\" Binding=\"{Binding TimestampUtc, StringFormat={}{0:u}}\"");
        }

        private static string ExtractTemplate(string xaml, string dataType)
        {
            string marker = $"<DataTemplate DataType=\"{{x:Type {dataType}}}\">";
            int start = xaml.IndexOf(marker, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, $"Could not find template for {dataType}.");

            int end = FindMatchingDataTemplateEnd(xaml, start);
            Assert.IsTrue(end > start, $"Could not find end of template for {dataType}.");

            return xaml.Substring(start, end - start);
        }

        private static string ExtractSection(string xaml, string title)
        {
            string marker = $"<styles:SHSection Title=\"{title}\">";
            int start = xaml.IndexOf(marker, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, $"Could not find section {title}.");

            int end = xaml.IndexOf("</styles:SHSection>", start, StringComparison.Ordinal);
            Assert.IsTrue(end > start, $"Could not find end of section {title}.");

            return xaml.Substring(start, end - start);
        }

        private static string ExtractElementStart(string xaml, string elementName)
        {
            string marker = $"<{elementName} ";
            int start = xaml.IndexOf(marker, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, $"Could not find {elementName}.");

            int end = xaml.IndexOf(">", start, StringComparison.Ordinal);
            Assert.IsTrue(end > start, $"Could not find end of {elementName} start tag.");

            return xaml.Substring(start, end - start);
        }

        private static int FindMatchingDataTemplateEnd(string xaml, int start)
        {
            int depth = 0;
            int position = start;

            while (position >= 0 && position < xaml.Length)
            {
                int nextOpen = xaml.IndexOf("<DataTemplate", position, StringComparison.Ordinal);
                int nextClose = xaml.IndexOf("</DataTemplate>", position, StringComparison.Ordinal);

                if (nextClose < 0)
                {
                    return -1;
                }

                if (nextOpen >= 0 && nextOpen < nextClose)
                {
                    depth++;
                    position = nextOpen + "<DataTemplate".Length;
                    continue;
                }

                depth--;
                position = nextClose + "</DataTemplate>".Length;
                if (depth == 0)
                {
                    return position;
                }
            }

            return -1;
        }

        private static string FindSettingsControlXamlPath()
        {
            DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "StatsPlus", "SettingsControl.xaml");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            Assert.Fail("Could not locate StatsPlus\\SettingsControl.xaml from test output directory.");
            return null;
        }
    }
}
