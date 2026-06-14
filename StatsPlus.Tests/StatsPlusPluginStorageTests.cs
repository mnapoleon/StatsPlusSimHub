using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
        public void MigrateFileIfNeeded_MovesLegacyCommonRootFileWhenTargetMissing()
        {
            string targetPath = Path.Combine(_tempDirectory, "PluginsData", "StatsPlus", "StatsPlus.laps.db");
            string legacyPath = Path.Combine(_tempDirectory, "PluginsData", "Common", "StatsPlus.laps.db");
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath));
            File.WriteAllText(legacyPath, "legacy-db");

            StatsPlusPlugin.MigrateFileIfNeeded(targetPath, legacyPath);

            Assert.IsFalse(File.Exists(legacyPath));
            Assert.AreEqual("legacy-db", File.ReadAllText(targetPath));
        }

        [TestMethod]
        public void MigrateFileIfNeeded_LeavesLegacyFileWhenTargetAlreadyExists()
        {
            string targetPath = Path.Combine(_tempDirectory, "PluginsData", "StatsPlus", "StatsPlus.laps.db");
            string legacyPath = Path.Combine(_tempDirectory, "PluginsData", "Common", "StatsPlus.laps.db");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath));
            File.WriteAllText(targetPath, "new-db");
            File.WriteAllText(legacyPath, "legacy-db");

            StatsPlusPlugin.MigrateFileIfNeeded(targetPath, legacyPath);

            Assert.AreEqual("new-db", File.ReadAllText(targetPath));
            Assert.AreEqual("legacy-db", File.ReadAllText(legacyPath));
        }

        [TestMethod]
        public void ResolveLegacyDataPath_PrefersStatsPlusFolderThenCommonSubfolderThenCommonRoot()
        {
            string statsPlusRoot = Path.Combine(_tempDirectory, "PluginsData", "StatsPlus");
            string commonRoot = Path.Combine(_tempDirectory, "PluginsData", "Common");
            string statsPlusPath = Path.Combine(statsPlusRoot, "StatsPlus.laps.json");
            string commonSubfolderPath = Path.Combine(commonRoot, "StatsPlus", "StatsPlus.laps.json");
            string commonRootPath = Path.Combine(commonRoot, "StatsPlus.laps.json");
            Directory.CreateDirectory(Path.GetDirectoryName(commonSubfolderPath));
            File.WriteAllText(commonRootPath, "root");

            Assert.AreEqual(
                commonRootPath,
                StatsPlusPlugin.ResolveLegacyDataPath(statsPlusRoot, commonRoot));

            File.WriteAllText(commonSubfolderPath, "subfolder");
            Assert.AreEqual(
                commonSubfolderPath,
                StatsPlusPlugin.ResolveLegacyDataPath(statsPlusRoot, commonRoot));

            Directory.CreateDirectory(statsPlusRoot);
            File.WriteAllText(statsPlusPath, "statsplus");
            Assert.AreEqual(
                statsPlusPath,
                StatsPlusPlugin.ResolveLegacyDataPath(statsPlusRoot, commonRoot));
        }

        [TestMethod]
        public void BackupFileIfPresent_CreatesAndOverwritesRollingBackup()
        {
            string databasePath = Path.Combine(_tempDirectory, "PluginsData", "StatsPlus", "StatsPlus.laps.db");
            string backupPath = databasePath + ".bak";
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath));
            File.WriteAllText(databasePath, "fresh-db");
            File.WriteAllText(backupPath, "stale-db");

            StatsPlusPlugin.BackupFileIfPresent(databasePath, backupPath);

            Assert.AreEqual("fresh-db", File.ReadAllText(backupPath));
        }
    }
}
