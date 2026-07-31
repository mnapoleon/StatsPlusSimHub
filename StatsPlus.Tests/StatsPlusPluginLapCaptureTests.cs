using System;
using System.IO;
using System.Linq;
using GameReaderCommon;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimHub.Plugins;

namespace StatsPlus.Tests
{
    [TestClass]
    public class StatsPlusPluginLapCaptureTests
    {
        private string _tempDirectory;
        private StatsPlusPlugin _plugin;
        private PluginManager _pluginManager;

        [TestInitialize]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "StatsPlusPluginLapCaptureTests", Guid.NewGuid().ToString("N"));
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
        public void DataUpdate_DoesNotDuplicatePreviousLapTimeWhenLmuLastLapTimeLagsOneTick()
        {
            var plugin = CreateInitializedPlugin();
            var pluginManager = _pluginManager;

            SendUpdate(plugin, pluginManager, 0, 0, 0.0, 0.0);
            SendUpdate(plugin, pluginManager, 0, 1, 0.0, 90.0);
            SendUpdate(plugin, pluginManager, 1, 1, 90.0, 90.0);
            SendUpdate(plugin, pluginManager, 1, 2, 90.0, 90.0);
            SendUpdate(plugin, pluginManager, 2, 2, 90.0, 90.0);
            SendUpdate(plugin, pluginManager, 2, 2, 90.0, 88.0);

            var summary = plugin.GameHistoryTabs.Single().Tracks.Single();
            plugin.SelectedTrackSummary = summary;

            var laps = plugin.SelectedTrackLaps.OrderBy(lap => lap.LapNumber).ToList();

            Assert.AreEqual(2, laps.Count);
            Assert.AreEqual(1, laps[0].LapNumber);
            Assert.AreEqual(90.0, laps[0].LapTimeSeconds, 0.0001);
            Assert.AreEqual(2, laps[1].LapNumber);
            Assert.AreEqual(88.0, laps[1].LapTimeSeconds, 0.0001);
        }

        [TestMethod]
        public void DataUpdate_DoesNotPromoteIncompleteInitialOutLapBoundaryIntoLapOne()
        {
            var plugin = CreateInitializedPlugin();
            var pluginManager = _pluginManager;

            SendUpdate(plugin, pluginManager, 0, 0, 0.0, 0.0);
            SendUpdate(plugin, pluginManager, 0, 1, 0.0, 0.0);
            SendUpdate(plugin, pluginManager, 1, 1, 0.0, 0.0);
            SendUpdate(plugin, pluginManager, 1, 2, 0.0, 76.9860, 28.773, 15.469);
            SendUpdate(plugin, pluginManager, 2, 2, 76.9860, 76.9860);
            SendUpdate(plugin, pluginManager, 2, 3, 76.9860, 72.0470, 25.866, 15.520);
            SendUpdate(plugin, pluginManager, 3, 3, 72.0470, 72.0470);

            var summary = plugin.GameHistoryTabs.Single().Tracks.Single();
            plugin.SelectedTrackSummary = summary;

            var laps = plugin.SelectedTrackLaps.OrderBy(lap => lap.LapNumber).ToList();
            string lapSummary = string.Join(", ", laps.Select(lap => $"{lap.LapNumber}:{lap.LapTimeSeconds:F3}:{lap.Sector1Seconds:F3}:{lap.Sector2Seconds:F3}"));

            Assert.AreEqual(2, laps.Count, lapSummary);
            Assert.AreEqual(2, laps[0].LapNumber, lapSummary);
            Assert.AreEqual(76.9860, laps[0].LapTimeSeconds, 0.0001, lapSummary);
            Assert.AreEqual(28.773, laps[0].Sector1Seconds, 0.0001, lapSummary);
            Assert.AreEqual(15.469, laps[0].Sector2Seconds, 0.0001, lapSummary);
            Assert.AreEqual(3, laps[1].LapNumber, lapSummary);
            Assert.AreEqual(72.0470, laps[1].LapTimeSeconds, 0.0001, lapSummary);
        }

        [TestMethod]
        public void DataUpdate_RecordsAssettoCorsaLapWhenContextDropsOnLapBoundary()
        {
            var plugin = CreateInitializedPlugin();
            var pluginManager = _pluginManager;

            SendUpdate(plugin, pluginManager, 0, 0, 0.0, 0.0, gameName: "AssettoCorsa", carModel: "Mazda MX-5", trackName: "Spa", trackNameWithConfig: "Spa GP");
            SendUpdate(plugin, pluginManager, 0, 1, 0.0, 90.0, gameName: "AssettoCorsa", carModel: "Mazda MX-5", trackName: "Spa", trackNameWithConfig: "Spa GP");
            SendUpdate(plugin, pluginManager, 1, 1, 90.0, 90.0, gameName: "AssettoCorsa", carModel: "Mazda MX-5", trackName: "Spa", trackNameWithConfig: "Spa GP");
            SendUpdate(plugin, pluginManager, 1, 2, 90.0, 90.0, gameName: "AssettoCorsa", carModel: string.Empty, trackName: "Spa", trackNameWithConfig: "Spa GP");
            SendUpdate(plugin, pluginManager, 2, 2, 90.0, 88.0, gameName: "AssettoCorsa", carModel: "Mazda MX-5", trackName: "Spa", trackNameWithConfig: "Spa GP");

            var summary = plugin.GameHistoryTabs.Single().Tracks.Single();
            plugin.SelectedTrackSummary = summary;

            var laps = plugin.SelectedTrackLaps.OrderBy(lap => lap.LapNumber).ToList();
            string lapSummary = string.Join(", ", laps.Select(lap => $"{lap.LapNumber}:{lap.LapTimeSeconds:F3}"));

            Assert.AreEqual(2, laps.Count, lapSummary);
            Assert.AreEqual(1, laps[0].LapNumber, lapSummary);
            Assert.AreEqual(90.0, laps[0].LapTimeSeconds, 0.0001, lapSummary);
            Assert.AreEqual(2, laps[1].LapNumber, lapSummary);
            Assert.AreEqual(88.0, laps[1].LapTimeSeconds, 0.0001, lapSummary);
        }

        [TestMethod]
        public void DataUpdate_RecordsAssettoCorsaFirstLapWhenInitialLastLapTimeLagsOneTick()
        {
            var plugin = CreateInitializedPlugin();
            var pluginManager = _pluginManager;

            SendUpdate(plugin, pluginManager, 0, 0, 0.0, 0.0, gameName: "AssettoCorsa", carModel: "Mazda MX-5", trackName: "Spa", trackNameWithConfig: "Spa GP");
            SendUpdate(plugin, pluginManager, 0, 1, 0.0, 0.0, gameName: "AssettoCorsa", carModel: "Mazda MX-5", trackName: "Spa", trackNameWithConfig: "Spa GP");
            SendUpdate(plugin, pluginManager, 1, 1, 0.0, 0.0, gameName: "AssettoCorsa", carModel: "Mazda MX-5", trackName: "Spa", trackNameWithConfig: "Spa GP");
            SendUpdate(plugin, pluginManager, 1, 1, 0.0, 90.0, gameName: "AssettoCorsa", carModel: "Mazda MX-5", trackName: "Spa", trackNameWithConfig: "Spa GP");

            Assert.AreEqual(1, plugin.GameHistoryTabs.Sum(tab => tab.Tracks.Sum(track => track.LapCount)));

            var summary = plugin.GameHistoryTabs.Single().Tracks.Single();
            plugin.SelectedTrackSummary = summary;

            var laps = plugin.SelectedTrackLaps.OrderBy(lap => lap.LapNumber).ToList();
            string lapSummary = string.Join(", ", laps.Select(lap => $"{lap.LapNumber}:{lap.LapTimeSeconds:F3}"));

            Assert.AreEqual(1, laps.Count, lapSummary);
            Assert.AreEqual(1, laps[0].LapNumber, lapSummary);
            Assert.AreEqual(90.0, laps[0].LapTimeSeconds, 0.0001, lapSummary);
        }

        [TestMethod]
        public void DataUpdate_RecordsPendingAssettoCorsaLapWhenGameStopsAfterFinish()
        {
            var plugin = CreateInitializedPlugin();
            var pluginManager = _pluginManager;

            SendUpdate(plugin, pluginManager, 0, 0, 0.0, 0.0, gameName: "AssettoCorsa", carModel: "Mazda MX-5", trackName: "Spa", trackNameWithConfig: "Spa GP");
            SendUpdate(plugin, pluginManager, 0, 1, 0.0, 90.0, gameName: "AssettoCorsa", carModel: "Mazda MX-5", trackName: "Spa", trackNameWithConfig: "Spa GP");
            SendUpdate(plugin, pluginManager, 1, 1, 90.0, 90.0, gameName: "AssettoCorsa", carModel: "Mazda MX-5", trackName: "Spa", trackNameWithConfig: "Spa GP");
            SendUpdate(plugin, pluginManager, 1, 2, 90.0, 90.0, gameName: "AssettoCorsa", carModel: "Mazda MX-5", trackName: "Spa", trackNameWithConfig: "Spa GP");
            SendUpdate(plugin, pluginManager, 2, 2, 90.0, 88.0, gameRunning: false, gameName: "AssettoCorsa", carModel: "Mazda MX-5", trackName: "Spa", trackNameWithConfig: "Spa GP");

            var summary = plugin.GameHistoryTabs.Single().Tracks.Single();
            plugin.SelectedTrackSummary = summary;

            var laps = plugin.SelectedTrackLaps.OrderBy(lap => lap.LapNumber).ToList();
            string lapSummary = string.Join(", ", laps.Select(lap => $"{lap.LapNumber}:{lap.LapTimeSeconds:F3}"));

            Assert.AreEqual(2, laps.Count, lapSummary);
            Assert.AreEqual(1, laps[0].LapNumber, lapSummary);
            Assert.AreEqual(90.0, laps[0].LapTimeSeconds, 0.0001, lapSummary);
            Assert.AreEqual(2, laps[1].LapNumber, lapSummary);
            Assert.AreEqual(88.0, laps[1].LapTimeSeconds, 0.0001, lapSummary);
        }

        [TestMethod]
        public void DataUpdate_RecordsAssettoCorsaLapsWhenBoundaryLastLapTimeIsStableButNew()
        {
            var plugin = CreateInitializedPlugin();
            var pluginManager = _pluginManager;

            SendUpdate(plugin, pluginManager, 0, 0, 0.0, 0.0, gameName: "AssettoCorsa", carModel: "GT Tornado V12", trackName: "ks_brands_hatch", trackNameWithConfig: "ks_brands_hatch-indy");
            SendUpdate(plugin, pluginManager, 0, 1, 48.815, 48.815, gameName: "AssettoCorsa", carModel: "GT Tornado V12", trackName: "ks_brands_hatch", trackNameWithConfig: "ks_brands_hatch-indy");
            SendUpdate(plugin, pluginManager, 1, 1, 48.815, 48.815, gameName: "AssettoCorsa", carModel: "GT Tornado V12", trackName: "ks_brands_hatch", trackNameWithConfig: "ks_brands_hatch-indy");
            SendUpdate(plugin, pluginManager, 1, 2, 48.265, 48.265, gameName: "AssettoCorsa", carModel: "GT Tornado V12", trackName: "ks_brands_hatch", trackNameWithConfig: "ks_brands_hatch-indy");
            SendUpdate(plugin, pluginManager, 2, 2, 48.265, 48.265, gameName: "AssettoCorsa", carModel: "GT Tornado V12", trackName: "ks_brands_hatch", trackNameWithConfig: "ks_brands_hatch-indy");

            var summary = plugin.GameHistoryTabs.Single().Tracks.Single();
            plugin.SelectedTrackSummary = summary;

            var laps = plugin.SelectedTrackLaps.OrderBy(lap => lap.LapNumber).ToList();
            string lapSummary = string.Join(", ", laps.Select(lap => $"{lap.LapNumber}:{lap.LapTimeSeconds:F3}"));

            Assert.AreEqual(2, laps.Count, lapSummary);
            Assert.AreEqual(1, laps[0].LapNumber, lapSummary);
            Assert.AreEqual(48.815, laps[0].LapTimeSeconds, 0.0001, lapSummary);
            Assert.AreEqual(2, laps[1].LapNumber, lapSummary);
            Assert.AreEqual(48.265, laps[1].LapTimeSeconds, 0.0001, lapSummary);
        }

        [TestMethod]
        public void DataUpdate_RecordsAssettoCorsaBackToBackLapsWithSameTimeWhenSectorsChange()
        {
            var plugin = CreateInitializedPlugin();
            var pluginManager = _pluginManager;

            SendUpdate(plugin, pluginManager, 0, 0, 0.0, 0.0, gameName: "AssettoCorsa", carModel: "GT Tornado V12", trackName: "ks_brands_hatch", trackNameWithConfig: "ks_brands_hatch-indy");
            SendUpdate(plugin, pluginManager, 0, 1, 48.815, 48.815, 28.500, 20.315, gameName: "AssettoCorsa", carModel: "GT Tornado V12", trackName: "ks_brands_hatch", trackNameWithConfig: "ks_brands_hatch-indy");
            SendUpdate(plugin, pluginManager, 1, 1, 48.815, 48.815, gameName: "AssettoCorsa", carModel: "GT Tornado V12", trackName: "ks_brands_hatch", trackNameWithConfig: "ks_brands_hatch-indy");
            SendUpdate(plugin, pluginManager, 1, 2, 48.815, 48.815, 27.000, 21.815, gameName: "AssettoCorsa", carModel: "GT Tornado V12", trackName: "ks_brands_hatch", trackNameWithConfig: "ks_brands_hatch-indy");
            SendUpdate(plugin, pluginManager, 2, 2, 48.815, 48.815, gameName: "AssettoCorsa", carModel: "GT Tornado V12", trackName: "ks_brands_hatch", trackNameWithConfig: "ks_brands_hatch-indy");

            var summary = plugin.GameHistoryTabs.Single().Tracks.Single();
            plugin.SelectedTrackSummary = summary;

            var laps = plugin.SelectedTrackLaps.OrderBy(lap => lap.LapNumber).ToList();
            string lapSummary = string.Join(", ", laps.Select(lap => $"{lap.LapNumber}:{lap.LapTimeSeconds:F3}:{lap.Sector1Seconds:F3}:{lap.Sector2Seconds:F3}"));

            Assert.AreEqual(2, laps.Count, lapSummary);
            Assert.AreEqual(1, laps[0].LapNumber, lapSummary);
            Assert.AreEqual(48.815, laps[0].LapTimeSeconds, 0.0001, lapSummary);
            Assert.AreEqual(28.500, laps[0].Sector1Seconds, 0.0001, lapSummary);
            Assert.AreEqual(2, laps[1].LapNumber, lapSummary);
            Assert.AreEqual(48.815, laps[1].LapTimeSeconds, 0.0001, lapSummary);
            Assert.AreEqual(27.000, laps[1].Sector1Seconds, 0.0001, lapSummary);
        }

        [TestMethod]
        public void DataUpdate_WritesStatsPlusDiagnosticLogForLapCaptureEvents()
        {
            var plugin = CreateInitializedPlugin();
            var pluginManager = _pluginManager;

            SendUpdate(plugin, pluginManager, 0, 0, 0.0, 0.0, gameName: "AssettoCorsa", carModel: "Mazda MX-5", trackName: "Spa", trackNameWithConfig: "Spa GP");
            SendUpdate(plugin, pluginManager, 0, 1, 0.0, 0.0, gameName: "AssettoCorsa", carModel: "Mazda MX-5", trackName: "Spa", trackNameWithConfig: "Spa GP");
            SendUpdate(plugin, pluginManager, 1, 1, 0.0, 0.0, gameName: "AssettoCorsa", carModel: "Mazda MX-5", trackName: "Spa", trackNameWithConfig: "Spa GP");
            SendUpdate(plugin, pluginManager, 1, 1, 0.0, 90.0, gameName: "AssettoCorsa", carModel: "Mazda MX-5", trackName: "Spa", trackNameWithConfig: "Spa GP");

            string logPath = Path.Combine(_tempDirectory, "PluginsData", "StatsPlus", "StatsPlus.diagnostics.log");
            Assert.IsTrue(File.Exists(logPath), logPath);

            string log = File.ReadAllText(logPath);
            StringAssert.Contains(log, "CONTEXT SWITCH");
            StringAssert.Contains(log, "LAP BOUNDARY");
            StringAssert.Contains(log, "PENDING QUEUED");
            StringAssert.Contains(log, "PENDING WAIT");
            StringAssert.Contains(log, "PENDING SAVED");
            StringAssert.Contains(log, "game=AssettoCorsa");
            StringAssert.Contains(log, "lap=1");
        }

        private static void SendUpdate(
            StatsPlusPlugin plugin,
            PluginManager pluginManager,
            int oldCompletedLaps,
            int newCompletedLaps,
            double oldLastLapSeconds,
            double newLastLapSeconds,
            double newSector1Seconds = 0.0,
            double newSector2Seconds = 0.0,
            bool gameRunning = true,
            string gameName = "LMU",
            string carModel = "Ferrari 499P",
            string trackName = "Le Mans",
            string trackNameWithConfig = "Le Mans - 24h")
        {
            var gameData = new GameData
            {
                GameRunning = gameRunning,
                GameName = gameName,
                OldData = CreateStatusData(oldCompletedLaps, oldLastLapSeconds, carModel: carModel, trackName: trackName, trackNameWithConfig: trackNameWithConfig),
                NewData = CreateStatusData(newCompletedLaps, newLastLapSeconds, newSector1Seconds, newSector2Seconds, carModel, trackName, trackNameWithConfig)
            };

            plugin.DataUpdate(pluginManager, ref gameData);
        }

        private StatsPlusPlugin CreateInitializedPlugin()
        {
            _plugin = new StatsPlusPlugin();
            _pluginManager = new TestPluginManager(_tempDirectory);
            _plugin.Init(_pluginManager);
            return _plugin;
        }

        private static TestStatusData CreateStatusData(
            int completedLaps,
            double lastLapSeconds,
            double sector1Seconds = 0.0,
            double sector2Seconds = 0.0,
            string carModel = "Ferrari 499P",
            string trackName = "Le Mans",
            string trackNameWithConfig = "Le Mans - 24h")
        {
            return new TestStatusData
            {
                CarModel = carModel,
                TrackName = trackName,
                TrackNameWithConfig = trackNameWithConfig,
                CompletedLaps = completedLaps,
                LastLapTime = lastLapSeconds > 0 ? TimeSpan.FromSeconds(lastLapSeconds) : (TimeSpan?)null,
                Sector1Time = sector1Seconds > 0 ? TimeSpan.FromSeconds(sector1Seconds) : (TimeSpan?)null,
                Sector2Time = sector2Seconds > 0 ? TimeSpan.FromSeconds(sector2Seconds) : (TimeSpan?)null,
                IsLapValid = true
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

            public override string GetCommonStoragePath(params string[] pathParts)
            {
                Directory.CreateDirectory(_commonStorageRoot);
                return Path.Combine(new[] { _commonStorageRoot }.Concat(pathParts ?? Array.Empty<string>()).ToArray());
            }

            public override string GetCommonStoragePath(bool create, params string[] pathParts)
            {
                return GetCommonStoragePath(pathParts);
            }
        }
    }
}
