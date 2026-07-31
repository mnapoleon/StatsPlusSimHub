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

        [TestMethod]
        public void DataUpdate_WhenCarOrTrackContextIsMissing_SetsStandbyRed()
        {
            StatsPlusPlugin plugin = CreateInitializedPlugin();
            var gameData = CreateGameData(gameRunning: true, gameName: "LMU");
            gameData.NewData.CarModel = string.Empty;

            plugin.DataUpdate(_pluginManager, ref gameData);

            Assert.IsFalse(plugin.IsTelemetryActive);
            Assert.AreEqual("Standby", plugin.LiveStatusLabel);
            Assert.AreSame(Brushes.Red, plugin.StatusSectionForeground);
            Assert.AreEqual("Waiting for game, car, and track context", plugin.DataStatus);
        }

        [TestMethod]
        public void DataUpdate_WhenTelemetryUpdateThrowsAfterRecording_SetsWaitingStatus()
        {
            StatsPlusPlugin plugin = CreateInitializedPlugin();
            var gameData = CreateGameData(gameRunning: true, gameName: "LMU");

            plugin.DataUpdate(_pluginManager, ref gameData);
            _pluginManager.ThrowOnPropertyName = "StatsPlus.SpeedKmh";
            plugin.DataUpdate(_pluginManager, ref gameData);

            Assert.IsFalse(plugin.IsTelemetryActive);
            Assert.AreEqual("Standby", plugin.LiveStatusLabel);
            Assert.AreEqual("Waiting for telemetry", plugin.DataStatus);
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

            public string ThrowOnPropertyName { get; set; }

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
                if (string.Equals(propertyName, ThrowOnPropertyName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Test telemetry update failure.");
                }

                Properties[propertyName] = value;
            }
        }
    }
}
