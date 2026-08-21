using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace StatsPlus.Tests
{
    [TestClass]
    public class StatsPlusGameProfileTests
    {
        [TestMethod]
        public void Resolve_RecognizesSupportedGameAliasesAndSettingsKeys()
        {
            StatsPlusGameProfileRegistry registry = StatsPlusGameProfileRegistry.CreateDefault();

            Assert.AreEqual("assettocorsa", registry.Resolve("Assetto Corsa").SettingsKey);
            Assert.AreEqual("assettocorsacompetizione", registry.Resolve("AssettoCorsaCompetizione").SettingsKey);
            Assert.AreEqual("assettocorsaevo", registry.Resolve("Assetto Corsa EVO").SettingsKey);
            Assert.AreEqual("automobilista2", registry.Resolve("Automobilista2").SettingsKey);
            Assert.AreEqual("iracing", registry.Resolve("iRacing").SettingsKey);
            Assert.AreEqual("lmu", registry.Resolve("LMU").SettingsKey);
            Assert.AreEqual("lmu", registry.Resolve("Le Mans Ultimate").SettingsKey);
            Assert.AreEqual("rfactor2", registry.Resolve("RFactor2").SettingsKey);
            Assert.AreEqual("raceroomracingexperience", registry.Resolve("R3E").SettingsKey);
            Assert.AreEqual(string.Empty, registry.Resolve("UnknownGame").SettingsKey);
        }

        [TestMethod]
        public void Resolve_UsesProfileRecordingToggles()
        {
            StatsPlusGameProfileRegistry registry = StatsPlusGameProfileRegistry.CreateDefault();
            PluginSettings settings = new PluginSettings
            {
                RecordAssettoCorsa = false,
                RecordLeMansUltimate = true,
                RecordR3E = false
            };

            Assert.IsFalse(registry.Resolve("AssettoCorsa").IsRecordingEnabled(settings));
            Assert.IsTrue(registry.Resolve("Le Mans Ultimate").IsRecordingEnabled(settings));
            Assert.IsFalse(registry.Resolve("RRRE").IsRecordingEnabled(settings));
            Assert.IsFalse(registry.Resolve("UnknownGame").IsRecordingEnabled(settings));
        }

        [TestMethod]
        public void AssettoCorsaProfile_MapsClassicAndEvoTrackDisplaysOnly()
        {
            StatsPlusGameProfileRegistry registry = StatsPlusGameProfileRegistry.CreateDefault();
            Dictionary<string, string> trackMap = new Dictionary<string, string>
            {
                ["ks_brands_hatch-indy"] = "Brands Hatch - Indy"
            };

            StatsPlusTrackDisplayContext context = new StatsPlusTrackDisplayContext(trackMap);

            Assert.AreEqual("Brands Hatch - Indy", registry.Resolve("AssettoCorsa").GetTrackDisplayName("ks_brands_hatch-indy", context));
            Assert.AreEqual("Brands Hatch - Indy", registry.Resolve("Assetto Corsa EVO").GetTrackDisplayName("ks_brands_hatch-indy", context));
            Assert.AreEqual("ks_brands_hatch-indy", registry.Resolve("Assetto Corsa Competizione").GetTrackDisplayName("ks_brands_hatch-indy", context));
        }

        [TestMethod]
        public void CircuitDisplay_DuplicatesSameDisplayGamesWithoutNormalizingUnderscores()
        {
            StatsPlusGameProfileRegistry registry = StatsPlusGameProfileRegistry.CreateDefault();

            CircuitDisplayParts acParts = registry.Resolve("AssettoCorsa").GetCircuitDisplayParts("monza_short");
            CircuitDisplayParts lmuParts = registry.Resolve("Le Mans Ultimate").GetCircuitDisplayParts("Le Mans - 24h");

            Assert.AreEqual("monza_short", acParts.CircuitNameDisplay);
            Assert.AreEqual("monza_short", acParts.CircuitLayoutDisplay);
            Assert.AreEqual("Le Mans - 24h", lmuParts.CircuitNameDisplay);
            Assert.AreEqual("Le Mans - 24h", lmuParts.CircuitLayoutDisplay);
        }

        [TestMethod]
        public void CircuitDisplay_SplitsGameSpecificLayouts()
        {
            StatsPlusGameProfileRegistry registry = StatsPlusGameProfileRegistry.CreateDefault();

            CircuitDisplayParts ams2Parts = registry.Resolve("Automobilista2").GetCircuitDisplayParts("Buenos_Aires-Buenos_Aires_Circuito_15");
            CircuitDisplayParts rfactorParts = registry.Resolve("RFactor2").GetCircuitDisplayParts("Lime Rock Park -- No Chicanes");
            CircuitDisplayParts iracingParts = registry.Resolve("IRacing").GetCircuitDisplayParts("spielberg_gp-Grand Prix");
            CircuitDisplayParts evoParts = registry.Resolve("Assetto Corsa EVO").GetCircuitDisplayParts("Brands Hatch - Indy");
            CircuitDisplayParts missingLayoutParts = registry.Resolve("Automobilista2").GetCircuitDisplayParts("Nurburgring");

            Assert.AreEqual("Buenos Aires", ams2Parts.CircuitNameDisplay);
            Assert.AreEqual("Buenos Aires Circuito 15", ams2Parts.CircuitLayoutDisplay);
            Assert.AreEqual("Lime Rock Park", rfactorParts.CircuitNameDisplay);
            Assert.AreEqual("No Chicanes", rfactorParts.CircuitLayoutDisplay);
            Assert.AreEqual("Spielberg GP", iracingParts.CircuitNameDisplay);
            Assert.AreEqual("Grand Prix", iracingParts.CircuitLayoutDisplay);
            Assert.AreEqual("Brands Hatch", evoParts.CircuitNameDisplay);
            Assert.AreEqual("Indy", evoParts.CircuitLayoutDisplay);
            Assert.AreEqual("Nurburgring", missingLayoutParts.CircuitNameDisplay);
            Assert.AreEqual(string.Empty, missingLayoutParts.CircuitLayoutDisplay);
        }

        [TestMethod]
        public void InferSectorLayout_KeepsAssettoFamilyTwoSectorFallbackInProfile()
        {
            StatsPlusGameProfileRegistry registry = StatsPlusGameProfileRegistry.CreateDefault();
            double sector1 = 28.5;
            double sector2 = 0.0;
            double sector3 = 0.0;

            registry.Resolve("AssettoCorsa").InferSectorLayout(48.815, ref sector1, ref sector2, ref sector3);

            Assert.AreEqual(28.5, sector1, 0.0001);
            Assert.AreEqual(20.315, sector2, 0.0001);
            Assert.AreEqual(0.0, sector3, 0.0001);
        }

        [TestMethod]
        public void LapBoundaryEvidence_UsesCapturedSectorsOnlyForAssettoFamily()
        {
            StatsPlusGameProfileRegistry registry = StatsPlusGameProfileRegistry.CreateDefault();

            Assert.IsTrue(registry.Resolve("AssettoCorsa").UsesCapturedSectorsAsLapBoundaryEvidence);
            Assert.IsTrue(registry.Resolve("Assetto Corsa Competizione").UsesCapturedSectorsAsLapBoundaryEvidence);
            Assert.IsTrue(registry.Resolve("Assetto Corsa EVO").UsesCapturedSectorsAsLapBoundaryEvidence);
            Assert.IsFalse(registry.Resolve("Automobilista2").UsesCapturedSectorsAsLapBoundaryEvidence);
            Assert.IsFalse(registry.Resolve("LMU").UsesCapturedSectorsAsLapBoundaryEvidence);
            Assert.IsFalse(registry.Resolve("UnknownGame").UsesCapturedSectorsAsLapBoundaryEvidence);
        }
    }
}
