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
        public void StoredHistoryGrid_ColorsBestLapByValidity()
        {
            string xaml = File.ReadAllText(FindSettingsControlXamlPath());

            StringAssert.Contains(xaml, "Header=\"Best\"");
            StringAssert.Contains(xaml, "Binding=\"{Binding BestLapSeconds, Converter={StaticResource TimeSpanSecondsFormatter}}\"");
            StringAssert.Contains(xaml, "Binding=\"{Binding IsBestLapValid}\" Value=\"False\"");
            StringAssert.Contains(xaml, "Property=\"Foreground\" Value=\"LimeGreen\"");
            StringAssert.Contains(xaml, "Property=\"Foreground\" Value=\"Red\"");
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
