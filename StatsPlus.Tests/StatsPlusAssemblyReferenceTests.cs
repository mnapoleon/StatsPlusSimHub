using System;
using System.Linq;
using System.Reflection;
using GameReaderCommon;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimHub.Plugins;

namespace StatsPlus.Tests
{
    [TestClass]
    public class StatsPlusAssemblyReferenceTests
    {
        [TestMethod]
        public void StatsPlusAssembly_ReferencesSimHubRuntimeAssemblyIdentities()
        {
            var references = typeof(StatsPlusPlugin).Assembly.GetReferencedAssemblies();

            Version simHubPluginsVersion = references.Single(reference => reference.Name == "SimHub.Plugins").Version;
            Version gameReaderCommonVersion = references.Single(reference => reference.Name == "GameReaderCommon").Version;
            Version simHubLoggingVersion = references.Single(reference => reference.Name == "SimHub.Logging").Version;
            Version log4NetVersion = references.Single(reference => reference.Name == "log4net").Version;

            Assert.AreEqual(new Version(1, 0, 9735, 26972), simHubPluginsVersion);
            Assert.AreEqual(new Version(1, 0, 0, 0), gameReaderCommonVersion);
            Assert.AreEqual(new Version(1, 0, 0, 0), simHubLoggingVersion);
            Assert.AreEqual(new Version(2, 0, 15, 0), log4NetVersion);
        }

        [TestMethod]
        public void SimHubPluginStub_ExposesPluginManagerSetter()
        {
            PropertyInfo pluginManagerProperty = typeof(IPlugin).GetProperty(
                "PluginManager",
                BindingFlags.Instance | BindingFlags.Public);

            Assert.IsNotNull(pluginManagerProperty);
            Assert.AreEqual(typeof(PluginManager), pluginManagerProperty.PropertyType);
            Assert.IsNotNull(pluginManagerProperty.SetMethod);
        }

        [TestMethod]
        public void SimHubWpfSettingsV2Stub_InheritsWpfSettings()
        {
            CollectionAssert.Contains(typeof(IWPFSettingsV2).GetInterfaces(), typeof(IWPFSettings));
        }

        [TestMethod]
        public void GameDataStub_ExposesTelemetrySnapshotsAsFields()
        {
            Assert.IsNull(typeof(GameData).GetProperty("NewData", BindingFlags.Instance | BindingFlags.Public));
            Assert.IsNull(typeof(GameData).GetProperty("OldData", BindingFlags.Instance | BindingFlags.Public));

            FieldInfo newDataField = typeof(GameData).GetField("NewData", BindingFlags.Instance | BindingFlags.Public);
            FieldInfo oldDataField = typeof(GameData).GetField("OldData", BindingFlags.Instance | BindingFlags.Public);

            Assert.IsNotNull(newDataField);
            Assert.AreEqual(typeof(StatusDataBase), newDataField.FieldType);
            Assert.IsNotNull(oldDataField);
            Assert.AreEqual(typeof(StatusDataBase), oldDataField.FieldType);
        }

        [TestMethod]
        public void StatusDataBaseStub_ExposesLastLapTimeAsNonNullableTimeSpan()
        {
            PropertyInfo lastLapTimeProperty = typeof(StatusDataBase).GetProperty(
                "LastLapTime",
                BindingFlags.Instance | BindingFlags.Public);

            Assert.IsNotNull(lastLapTimeProperty);
            Assert.AreEqual(typeof(TimeSpan), lastLapTimeProperty.PropertyType);
        }
    }
}
