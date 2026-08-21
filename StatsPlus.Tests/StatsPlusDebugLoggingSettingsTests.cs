using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace StatsPlus.Tests
{
    [TestClass]
    public class StatsPlusDebugLoggingSettingsTests
    {
        [TestMethod]
        public void NewSettings_DisablesDebugLoggingByDefault()
        {
            PluginSettings settings = new PluginSettings();

            Assert.IsFalse(settings.EnableDebugLogging);
            Assert.IsNotNull(settings.GameDebugLogging);
            Assert.AreEqual(0, settings.GameDebugLogging.Count);
        }

        [TestMethod]
        public void Reset_DisablesDebugLoggingAndClearsGameSelections()
        {
            PluginSettings settings = new PluginSettings
            {
                EnableDebugLogging = true,
                GameDebugLogging = new Dictionary<string, bool>
                {
                    ["iracing"] = true
                }
            };

            settings.Reset();

            Assert.IsFalse(settings.EnableDebugLogging);
            Assert.IsNotNull(settings.GameDebugLogging);
            Assert.AreEqual(0, settings.GameDebugLogging.Count);
        }

        [TestMethod]
        public void EnsureDefaultGameDebugLoggingSettings_AddsSupportedGamesDisabled()
        {
            StatsPlusPlugin plugin = new StatsPlusPlugin();
            MethodInfo method = typeof(StatsPlusPlugin).GetMethod(
                "EnsureDefaultGameDebugLoggingSettings",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(method);
            method.Invoke(plugin, null);

            Assert.IsTrue(plugin.Settings.GameDebugLogging.ContainsKey("assettocorsa"));
            Assert.IsTrue(plugin.Settings.GameDebugLogging.ContainsKey("assettocorsacompetizione"));
            Assert.IsTrue(plugin.Settings.GameDebugLogging.ContainsKey("assettocorsaevo"));
            Assert.IsTrue(plugin.Settings.GameDebugLogging.ContainsKey("automobilista2"));
            Assert.IsTrue(plugin.Settings.GameDebugLogging.ContainsKey("iracing"));
            Assert.IsTrue(plugin.Settings.GameDebugLogging.ContainsKey("lmu"));
            Assert.IsTrue(plugin.Settings.GameDebugLogging.ContainsKey("rfactor2"));
            Assert.IsTrue(plugin.Settings.GameDebugLogging.ContainsKey("raceroomracingexperience"));
            Assert.IsFalse(plugin.Settings.GameDebugLogging.Values.Any(isEnabled => isEnabled));
        }

        [TestMethod]
        public void RefreshGameDebugLoggingOptions_RendersFriendlyLabelsUnchecked()
        {
            StatsPlusPlugin plugin = new StatsPlusPlugin();
            Invoke(plugin, "EnsureDefaultGameDebugLoggingSettings");
            Invoke(plugin, "RefreshGameDebugLoggingOptions");

            GameDebugLoggingOption option = plugin.GameDebugLoggingOptions
                .Single(entry => entry.SettingsKey == "raceroomracingexperience");

            Assert.AreEqual("RaceRoom Racing Experience", option.DisplayName);
            Assert.IsFalse(option.IsEnabled);
        }

        [TestMethod]
        public void RefreshGameDebugLoggingOptions_RendersAssettoCorsaCompetizioneLabel()
        {
            StatsPlusPlugin plugin = new StatsPlusPlugin();
            Invoke(plugin, "EnsureDefaultGameDebugLoggingSettings");
            Invoke(plugin, "RefreshGameDebugLoggingOptions");

            GameDebugLoggingOption option = plugin.GameDebugLoggingOptions
                .Single(entry => entry.SettingsKey == "assettocorsacompetizione");

            Assert.AreEqual("Assetto Corsa Competizione", option.DisplayName);
            Assert.IsFalse(option.IsEnabled);
        }

        [TestMethod]
        public void RefreshGameDebugLoggingOptions_MatchesSupportedProfiles()
        {
            StatsPlusPlugin plugin = new StatsPlusPlugin();
            StatsPlusGameProfileRegistry registry = StatsPlusGameProfileRegistry.CreateDefault();
            Invoke(plugin, "EnsureDefaultGameDebugLoggingSettings");
            Invoke(plugin, "RefreshGameDebugLoggingOptions");

            string[] expectedKeys = registry.SupportedProfiles
                .OrderBy(profile => profile.DisplayName)
                .Select(profile => profile.SettingsKey)
                .ToArray();
            string[] actualKeys = plugin.GameDebugLoggingOptions
                .Select(option => option.SettingsKey)
                .ToArray();

            CollectionAssert.AreEqual(expectedKeys, actualKeys);
        }

        [TestMethod]
        public void GameDebugLoggingOption_UpdateChangesSettingsDictionary()
        {
            StatsPlusPlugin plugin = new StatsPlusPlugin();
            Invoke(plugin, "EnsureDefaultGameDebugLoggingSettings");
            Invoke(plugin, "RefreshGameDebugLoggingOptions");

            GameDebugLoggingOption option = plugin.GameDebugLoggingOptions
                .Single(entry => entry.SettingsKey == "iracing");

            option.IsEnabled = true;

            Assert.IsTrue(plugin.Settings.GameDebugLogging["iracing"]);
        }

        [TestMethod]
        public void GetDebugLoggingSettingsKey_NormalizesRaceRoomAliases()
        {
            StatsPlusPlugin plugin = new StatsPlusPlugin();
            MethodInfo method = typeof(StatsPlusPlugin).GetMethod(
                "GetDebugLoggingSettingsKey",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.AreEqual("raceroomracingexperience", method.Invoke(plugin, new object[] { "R3E" }));
            Assert.AreEqual("raceroomracingexperience", method.Invoke(plugin, new object[] { "RRRE" }));
            Assert.AreEqual("raceroomracingexperience", method.Invoke(plugin, new object[] { "RaceRoom Racing Experience" }));
        }

        [TestMethod]
        public void EnsureGameDebugLoggingConfigured_UsesProfileSettingsKeyForAliases()
        {
            StatsPlusPlugin plugin = new StatsPlusPlugin();
            MethodInfo method = typeof(StatsPlusPlugin).GetMethod("EnsureGameDebugLoggingConfigured", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(method, "Expected StatsPlusPlugin.EnsureGameDebugLoggingConfigured to exist.");
            Assert.AreEqual(true, method.Invoke(plugin, new object[] { "RRRE" }));
            Assert.IsTrue(plugin.Settings.GameDebugLogging.ContainsKey("raceroomracingexperience"));
            Assert.IsFalse(plugin.Settings.GameDebugLogging.ContainsKey("rrre"));
        }

        [TestMethod]
        public void IsDebugLoggingEnabled_UpdatesSettingAndRaisesPropertyChanged()
        {
            StatsPlusPlugin plugin = new StatsPlusPlugin();
            List<string> changedProperties = new List<string>();
            plugin.PropertyChanged += (sender, args) => changedProperties.Add(args.PropertyName);

            plugin.IsDebugLoggingEnabled = true;
            plugin.IsDebugLoggingEnabled = false;

            CollectionAssert.AreEqual(
                new[] { nameof(StatsPlusPlugin.IsDebugLoggingEnabled), nameof(StatsPlusPlugin.IsDebugLoggingEnabled) },
                changedProperties.Where(name => name == nameof(StatsPlusPlugin.IsDebugLoggingEnabled)).ToList());
            Assert.IsFalse(plugin.Settings.EnableDebugLogging);
        }

        private static void Invoke(StatsPlusPlugin plugin, string methodName)
        {
            typeof(StatsPlusPlugin)
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(plugin, null);
        }
    }
}
