using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimHub.Plugins;

namespace StatsPlus.Tests
{
    [TestClass]
    public class StatsPlusPluginStorageTests
    {
        private string _tempDirectory;

        [TestInitialize]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "StatsPlusPluginStorageTests", Guid.NewGuid().ToString("N"));
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
        public void GetStatsPlusStorageRoot_ReturnsPluginsDataStatsPlusSiblingOfCommon()
        {
            string commonRoot = Path.Combine(_tempDirectory, "PluginsData", "Common");

            string result = StatsPlusPlugin.GetStatsPlusStorageRoot(commonRoot);

            Assert.AreEqual(Path.Combine(_tempDirectory, "PluginsData", "StatsPlus"), result);
        }

        [TestMethod]
        public void MigrateFileIfNeeded_MovesCommonRootFileWhenTargetMissing()
        {
            string targetPath = Path.Combine(_tempDirectory, "PluginsData", "StatsPlus", "StatsPlus.laps.ldb");
            string sourcePath = Path.Combine(_tempDirectory, "PluginsData", "Common", "StatsPlus.laps.ldb");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath));
            File.WriteAllText(sourcePath, "legacy-db");

            StatsPlusPlugin.MigrateFileIfNeeded(targetPath, sourcePath);

            Assert.IsFalse(File.Exists(sourcePath));
            Assert.AreEqual("legacy-db", File.ReadAllText(targetPath));
        }

        [TestMethod]
        public void MigrateFileIfNeeded_LeavesSourceFileWhenTargetAlreadyExists()
        {
            string targetPath = Path.Combine(_tempDirectory, "PluginsData", "StatsPlus", "StatsPlus.laps.ldb");
            string sourcePath = Path.Combine(_tempDirectory, "PluginsData", "Common", "StatsPlus.laps.ldb");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath));
            File.WriteAllText(targetPath, "new-db");
            File.WriteAllText(sourcePath, "legacy-db");

            StatsPlusPlugin.MigrateFileIfNeeded(targetPath, sourcePath);

            Assert.AreEqual("new-db", File.ReadAllText(targetPath));
            Assert.AreEqual("legacy-db", File.ReadAllText(sourcePath));
        }

        [TestMethod]
        public void BackupFileIfPresent_CreatesAndOverwritesRollingBackup()
        {
            string databasePath = Path.Combine(_tempDirectory, "PluginsData", "StatsPlus", "StatsPlus.laps.ldb");
            string backupPath = databasePath + ".bak";
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath));
            File.WriteAllText(databasePath, "fresh-db");
            File.WriteAllText(backupPath, "stale-db");

            StatsPlusPlugin.BackupFileIfPresent(databasePath, backupPath);

            Assert.AreEqual("fresh-db", File.ReadAllText(backupPath));
        }

        [TestMethod]
        public void Init_PublishesLiteDbDataFilePath()
        {
            var plugin = new StatsPlusPlugin();
            var pluginManager = new TestPluginManager(_tempDirectory);

            try
            {
                plugin.Init(pluginManager);

                Assert.AreEqual(
                    Path.Combine(_tempDirectory, "PluginsData", "StatsPlus", "StatsPlus.laps.ldb"),
                    pluginManager.Properties["StatsPlus.DataFilePath"]);
            }
            finally
            {
                plugin.End(pluginManager);
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

            public override void AddProperty<T>(string propertyName, Type ownerType, T initialValue, string unit = null)
            {
                Properties[propertyName] = initialValue;
            }
        }
    }
}
