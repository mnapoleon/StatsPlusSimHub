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
        public void RefreshStoredTrackSummaries_PopulatesAffinityStyleCircuitColumns()
        {
            SeedLap("Automobilista2", "Formula Trainer", "Buenos_Aires", "Buenos_Aires-Buenos_Aires_Circuito_15", 118.5, new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc));
            StatsPlusPlugin plugin = CreateInitializedPlugin();

            StoredTrackSummary summary = plugin.TopLevelTabs.OfType<GameHistoryTab>().Single().Tracks.Single();

            Assert.AreEqual("Buenos_Aires-Buenos_Aires_Circuito_15", summary.TrackNameWithConfig);
            Assert.AreEqual("Buenos Aires", summary.CircuitNameDisplay);
            Assert.AreEqual("Buenos Aires Circuito 15", summary.CircuitLayoutDisplay);
        }

        [TestMethod]
        public void SelectedTrackSummary_LoadsRecordedLapsAndPopulatesCircuitDisplayWithoutChangingLookup()
        {
            SeedLap("RFactor2", "BTCC", "Lime Rock Park", "Lime Rock Park -- No Chicanes", 62.25, new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc));
            StatsPlusPlugin plugin = CreateInitializedPlugin();
            GameHistoryTab tab = plugin.TopLevelTabs.OfType<GameHistoryTab>().Single();

            plugin.SelectedTrackSummary = tab.Tracks.Single();

            Assert.AreEqual(1, plugin.SelectedTrackLaps.Count);
            Assert.AreEqual("Lime Rock Park -- No Chicanes", plugin.SelectedTrackLaps[0].TrackNameWithConfig);
            Assert.AreEqual("Lime Rock Park", plugin.SelectedTrackLaps[0].CircuitNameDisplay);
            Assert.AreEqual("No Chicanes", plugin.SelectedTrackLaps[0].CircuitLayoutDisplay);
        }

        [TestMethod]
        public void SelectedTrackSummary_LoadsLapsByRawTrackIdentityWhenCircuitDisplaysCollide()
        {
            SeedLap("IRacing", "Mazda MX-5", "spielberg gp", "spielberg gp-Grand Prix", 91.25, new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc));
            SeedLap("IRacing", "Mazda MX-5", "spielberg_gp", "spielberg_gp-Grand Prix", 90.50, new DateTime(2026, 8, 21, 12, 5, 0, DateTimeKind.Utc));
            StatsPlusPlugin plugin = CreateInitializedPlugin();
            GameHistoryTab tab = plugin.TopLevelTabs.OfType<GameHistoryTab>().Single();

            StoredTrackSummary spacedRawTrack = tab.Tracks.Single(summary => summary.TrackNameWithConfig == "spielberg gp-Grand Prix");
            StoredTrackSummary underscoreRawTrack = tab.Tracks.Single(summary => summary.TrackNameWithConfig == "spielberg_gp-Grand Prix");

            Assert.AreEqual(spacedRawTrack.CircuitNameDisplay, underscoreRawTrack.CircuitNameDisplay);
            Assert.AreEqual(spacedRawTrack.CircuitLayoutDisplay, underscoreRawTrack.CircuitLayoutDisplay);
            Assert.AreEqual("Spielberg GP", spacedRawTrack.CircuitNameDisplay);
            Assert.AreEqual("Grand Prix", spacedRawTrack.CircuitLayoutDisplay);
            Assert.AreEqual("Spielberg GP", underscoreRawTrack.CircuitNameDisplay);
            Assert.AreEqual("Grand Prix", underscoreRawTrack.CircuitLayoutDisplay);

            plugin.SelectedTrackSummary = spacedRawTrack;
            Assert.AreEqual("spielberg gp-Grand Prix", plugin.SelectedTrackLaps[0].TrackNameWithConfig);
            Assert.AreEqual(91.25, plugin.SelectedTrackLaps[0].LapTimeSeconds, 0.0001);

            plugin.SelectedTrackSummary = underscoreRawTrack;
            Assert.AreEqual("spielberg_gp-Grand Prix", plugin.SelectedTrackLaps[0].TrackNameWithConfig);
            Assert.AreEqual(90.50, plugin.SelectedTrackLaps[0].LapTimeSeconds, 0.0001);
        }

        [TestMethod]
        public void PersonalBestProperties_KeepRawTrackIdentityAfterCircuitDisplayPopulation()
        {
            SeedLap("AssettoCorsa", "GT Tornado V12", "ks_brands_hatch", "ks_brands_hatch-indy", 48.265, new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc));
            StatsPlusPlugin plugin = CreateInitializedPlugin();
            StoredTrackSummary summary = plugin.TopLevelTabs.OfType<GameHistoryTab>().Single().Tracks.Single();

            Assert.AreEqual("Brands Hatch - Indy", summary.CircuitNameDisplay);
            Assert.AreEqual("Brands Hatch - Indy", summary.CircuitLayoutDisplay);

            Assert.IsTrue(_pluginManager.Properties.ContainsKey("StatsPlus.PersonalBest.AssettoCorsa.GT_Tornado_V12.ks_brands_hatch_indy"));
            Assert.IsFalse(_pluginManager.Properties.ContainsKey("StatsPlus.PersonalBest.AssettoCorsa.GT_Tornado_V12.Brands_Hatch_Indy"));
            Assert.AreEqual(48.265, (double)_pluginManager.Properties["StatsPlus.PersonalBest.AssettoCorsa.GT_Tornado_V12.ks_brands_hatch_indy"], 0.0001);
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

        private StatsPlusPlugin CreateInitializedPlugin()
        {
            _plugin = new StatsPlusPlugin();
            _pluginManager = new TestPluginManager(_tempDirectory);
            _plugin.Init(_pluginManager);
            return _plugin;
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
