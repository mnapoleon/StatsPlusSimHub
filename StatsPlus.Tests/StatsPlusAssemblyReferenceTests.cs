using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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

            Assert.AreEqual(new Version(1, 0, 9735, 26972), simHubPluginsVersion);
            Assert.AreEqual(new Version(1, 0, 0, 0), gameReaderCommonVersion);
        }
    }
}
