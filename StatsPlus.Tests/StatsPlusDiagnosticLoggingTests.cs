using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace StatsPlus.Tests
{
    [TestClass]
    public class StatsPlusDiagnosticLoggingTests
    {
        private string _tempDirectory;

        [TestInitialize]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "StatsPlusDiagnosticLoggingTests", Guid.NewGuid().ToString("N"));
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
        public void WriteDiagnosticLog_GlobalDisabledWritesNoFile()
        {
            StatsPlusPlugin plugin = CreatePluginWithDiagnosticPath();
            plugin.Settings.EnableDebugLogging = false;
            plugin.Settings.GameDebugLogging["iracing"] = true;

            InvokeWriteDiagnosticLog(plugin, "iRacing", "EVENT", "detail");

            Assert.IsFalse(File.Exists(Path.Combine(_tempDirectory, "StatsPlus.diagnostics.iracing.log")));
            Assert.IsFalse(File.Exists(Path.Combine(_tempDirectory, "StatsPlus.diagnostics.log")));
        }

        [TestMethod]
        public void WriteDiagnosticLog_GameDisabledWritesNoFile()
        {
            StatsPlusPlugin plugin = CreatePluginWithDiagnosticPath();
            plugin.Settings.EnableDebugLogging = true;
            plugin.Settings.GameDebugLogging["iracing"] = false;

            InvokeWriteDiagnosticLog(plugin, "iRacing", "EVENT", "detail");

            Assert.IsFalse(File.Exists(Path.Combine(_tempDirectory, "StatsPlus.diagnostics.iracing.log")));
            Assert.IsFalse(File.Exists(Path.Combine(_tempDirectory, "StatsPlus.diagnostics.log")));
        }

        [TestMethod]
        public void WriteDiagnosticLog_GameEnabledWritesGameSpecificFile()
        {
            StatsPlusPlugin plugin = CreatePluginWithDiagnosticPath();
            plugin.Settings.EnableDebugLogging = true;
            plugin.Settings.GameDebugLogging["iracing"] = true;

            InvokeWriteDiagnosticLog(plugin, "iRacing", "EVENT", "detail");

            string gameLogPath = Path.Combine(_tempDirectory, "StatsPlus.diagnostics.iracing.log");
            Assert.IsTrue(File.Exists(gameLogPath));
            StringAssert.Contains(File.ReadAllText(gameLogPath), "EVENT detail");
            Assert.IsFalse(File.Exists(Path.Combine(_tempDirectory, "StatsPlus.diagnostics.log")));
        }

        [TestMethod]
        public void GetDiagnosticLogPath_UsesRaceRoomCanonicalKey()
        {
            StatsPlusPlugin plugin = CreatePluginWithDiagnosticPath();
            MethodInfo method = typeof(StatsPlusPlugin).GetMethod(
                "GetDiagnosticLogPath",
                BindingFlags.Instance | BindingFlags.NonPublic);

            string result = (string)method.Invoke(plugin, new object[] { "R3E" });

            Assert.AreEqual(Path.Combine(_tempDirectory, "StatsPlus.diagnostics.raceroomracingexperience.log"), result);
        }

        private StatsPlusPlugin CreatePluginWithDiagnosticPath()
        {
            StatsPlusPlugin plugin = new StatsPlusPlugin();
            SetDiagnosticLogPath(plugin, Path.Combine(_tempDirectory, "StatsPlus.diagnostics.log"));
            Invoke(plugin, "EnsureDefaultGameDebugLoggingSettings");
            return plugin;
        }

        private static void SetDiagnosticLogPath(StatsPlusPlugin plugin, string path)
        {
            typeof(StatsPlusPlugin)
                .GetField("_diagnosticLogPath", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(plugin, path);
        }

        private static void InvokeWriteDiagnosticLog(StatsPlusPlugin plugin, string gameName, string eventName, string detail)
        {
            typeof(StatsPlusPlugin)
                .GetMethod("WriteDiagnosticLog", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(plugin, new object[] { gameName, eventName, detail });
        }

        private static void Invoke(StatsPlusPlugin plugin, string methodName)
        {
            typeof(StatsPlusPlugin)
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(plugin, null);
        }
    }
}
