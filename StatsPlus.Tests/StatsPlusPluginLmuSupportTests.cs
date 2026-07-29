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
        public void IsGameRecordingEnabled_RecognizesLmuToggle()
        {
            StatsPlusPlugin plugin = new StatsPlusPlugin();
            MethodInfo method = typeof(StatsPlusPlugin).GetMethod("IsGameRecordingEnabled", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(method, "Expected StatsPlusPlugin.IsGameRecordingEnabled to exist.");
            Assert.AreEqual(true, method.Invoke(plugin, new object[] { "LMU" }));
        }
    }
}
