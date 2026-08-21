using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace StatsPlus.Tests
{
    [TestClass]
    public class StatsPlusPluginLmuSupportTests
    {
        [TestMethod]
        public void PluginSettings_DefaultsLeMansUltimateRecordingToEnabled()
        {
            PluginSettings settings = new PluginSettings();
            PropertyInfo property = typeof(PluginSettings).GetProperty("RecordLeMansUltimate");

            Assert.IsNotNull(property, "Expected a RecordLeMansUltimate setting.");
            Assert.AreEqual(true, property.GetValue(settings));
        }

        [TestMethod]
        public void PluginSettings_DefaultsAssettoCorsaCompetizioneRecordingToEnabled()
        {
            PluginSettings settings = new PluginSettings();
            PropertyInfo property = typeof(PluginSettings).GetProperty("RecordAssettoCorsaCompetizione");

            Assert.IsNotNull(property, "Expected a RecordAssettoCorsaCompetizione setting.");
            Assert.AreEqual(true, property.GetValue(settings));
        }

        [TestMethod]
        public void IsGameRecordingEnabled_RecognizesLmuToggle()
        {
            StatsPlusPlugin plugin = new StatsPlusPlugin();
            MethodInfo method = typeof(StatsPlusPlugin).GetMethod("IsGameRecordingEnabled", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(method, "Expected StatsPlusPlugin.IsGameRecordingEnabled to exist.");
            Assert.AreEqual(true, method.Invoke(plugin, new object[] { "LMU" }));
        }

        [TestMethod]
        public void IsGameRecordingEnabled_RecognizesLeMansUltimateAlias()
        {
            StatsPlusPlugin plugin = new StatsPlusPlugin();
            MethodInfo method = typeof(StatsPlusPlugin).GetMethod("IsGameRecordingEnabled", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(method, "Expected StatsPlusPlugin.IsGameRecordingEnabled to exist.");
            Assert.AreEqual(true, method.Invoke(plugin, new object[] { "Le Mans Ultimate" }));
        }

        [TestMethod]
        public void IsGameRecordingEnabled_RecognizesAssettoCorsaCompetizioneToggle()
        {
            StatsPlusPlugin plugin = new StatsPlusPlugin();
            MethodInfo method = typeof(StatsPlusPlugin).GetMethod("IsGameRecordingEnabled", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(method, "Expected StatsPlusPlugin.IsGameRecordingEnabled to exist.");
            Assert.AreEqual(true, method.Invoke(plugin, new object[] { "AssettoCorsaCompetizione" }));
        }

        [TestMethod]
        public void GetDisplayTrackNameWithConfig_LeavesCompetizioneTrackNamesUntouched()
        {
            StatsPlusPlugin plugin = new StatsPlusPlugin();
            MethodInfo method = typeof(StatsPlusPlugin).GetMethod("GetDisplayTrackNameWithConfig", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(method, "Expected StatsPlusPlugin.GetDisplayTrackNameWithConfig to exist.");
            Assert.AreEqual(
                "ks_spa",
                method.Invoke(plugin, new object[] { "Assetto Corsa Competizione", "ks_spa" }));
        }
    }
}
