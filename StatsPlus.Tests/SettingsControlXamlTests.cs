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
