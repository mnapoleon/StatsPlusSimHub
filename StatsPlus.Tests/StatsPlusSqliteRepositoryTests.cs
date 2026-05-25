using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace StatsPlus.Tests
{
    [TestClass]
    public class StatsPlusSqliteRepositoryTests
    {
        private string _tempDirectory;
        private string _databasePath;

        [TestInitialize]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "StatsPlusTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
            _databasePath = Path.Combine(_tempDirectory, "StatsPlus.laps.db");
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
        public void ImportLegacyDatabase_PreservesLapsSummariesAndPersonalBests()
        {
            var createdUtc = new DateTime(2026, 5, 25, 0, 0, 0, DateTimeKind.Utc);
            var lastUpdatedUtc = createdUtc.AddMinutes(5);
            var newestLapTime = createdUtc.AddMinutes(4);
            var olderLapTime = createdUtc.AddMinutes(2);

            var legacy = new LapDatabase();
            legacy.Games["AssettoCorsa"] = new GameBucket
            {
                Cars =
                {
                    ["BMW M3 GT2"] = new CarBucket
                    {
                        Tracks =
                        {
                            ["spa-gp"] = new TrackBucket
                            {
                                GameName = "AssettoCorsa",
                                CarModel = "BMW M3 GT2",
                                TrackName = "spa",
                                TrackNameWithConfig = "spa-gp",
                                CreatedUtc = createdUtc,
                                LastUpdatedUtc = lastUpdatedUtc,
                                Laps =
                                {
                                    new RecordedLap
                                    {
                                        LapNumber = 1,
                                        LapTimeSeconds = 121.500,
                                        Sector1Seconds = 40.100,
                                        Sector2Seconds = 40.500,
                                        Sector3Seconds = 40.900,
                                        IsValid = true,
                                        TimestampUtc = olderLapTime
                                    },
                                    new RecordedLap
                                    {
                                        LapNumber = 2,
                                        LapTimeSeconds = 119.250,
                                        Sector1Seconds = 39.700,
                                        Sector2Seconds = 39.800,
                                        Sector3Seconds = 39.750,
                                        IsValid = false,
                                        TimestampUtc = newestLapTime
                                    }
                                }
                            }
                        }
                    }
                }
            };

            using (var repository = CreateRepository())
            {
                repository.ImportLegacyDatabase(legacy);

                Assert.IsTrue(repository.HasLapData());

                var summaries = repository.GetTrackSummaries();
                Assert.AreEqual(1, summaries.Count);
                Assert.AreEqual("AssettoCorsa", summaries[0].GameName);
                Assert.AreEqual("BMW M3 GT2", summaries[0].CarModel);
                Assert.AreEqual("spa-gp", summaries[0].TrackNameWithConfig);
                Assert.AreEqual(2, summaries[0].LapCount);
                Assert.AreEqual(121.500, summaries[0].BestLapSeconds, 0.0001);
                Assert.AreEqual(lastUpdatedUtc, summaries[0].LastRecordedUtc);

                var laps = repository.GetTrackLaps("assettocorsa", "bmw m3 gt2", "SPA-GP");
                Assert.AreEqual(2, laps.Count);
                Assert.AreEqual(2, laps[0].LapNumber);
                Assert.AreEqual(newestLapTime, laps[0].TimestampUtc);
                Assert.AreEqual(1, laps[1].LapNumber);

                var personalBests = repository.GetPersonalBestPropertyValues();
                Assert.AreEqual(1, personalBests.Count);
                Assert.AreEqual(121.500, personalBests["StatsPlus.PersonalBest.AssettoCorsa.BMW_M3_GT2.spa_gp"], 0.0001);
            }
        }

        [TestMethod]
        public void AddLap_UsesCaseInsensitiveIdentity_AndToggleValidityRecalculatesBestLap()
        {
            using (var repository = CreateRepository())
            {
                repository.AddLap("AssettoCorsa", "BMW M3 GT2", "spa", "spa-gp", new RecordedLap
                {
                    LapNumber = 1,
                    LapTimeSeconds = 122.0,
                    Sector1Seconds = 40.0,
                    Sector2Seconds = 41.0,
                    Sector3Seconds = 41.0,
                    IsValid = true,
                    TimestampUtc = new DateTime(2026, 5, 25, 1, 0, 0, DateTimeKind.Utc)
                });

                repository.AddLap("assettocorsa", "bmw m3 gt2", "spa", "SPA-GP", new RecordedLap
                {
                    LapNumber = 2,
                    LapTimeSeconds = 119.0,
                    Sector1Seconds = 39.0,
                    Sector2Seconds = 40.0,
                    Sector3Seconds = 40.0,
                    IsValid = true,
                    TimestampUtc = new DateTime(2026, 5, 25, 1, 2, 0, DateTimeKind.Utc)
                });

                var summaries = repository.GetTrackSummaries();
                Assert.AreEqual(1, summaries.Count);
                Assert.AreEqual(2, summaries[0].LapCount);
                Assert.AreEqual(119.0, repository.GetBestLapSeconds("ASSETTOCORSA", "BMW M3 GT2", "spa-gp"), 0.0001);

                var laps = repository.GetTrackLaps("AssettoCorsa", "BMW M3 GT2", "spa-gp");
                Assert.AreEqual(2, laps.Count);

                repository.ToggleLapValidity(laps[0].LapId);

                Assert.AreEqual(122.0, repository.GetBestLapSeconds("AssettoCorsa", "BMW M3 GT2", "SPA-GP"), 0.0001);
                Assert.AreEqual(122.0, repository.GetTrackSummaries().Single().BestLapSeconds, 0.0001);
                Assert.IsFalse(repository.GetTrackLaps("AssettoCorsa", "BMW M3 GT2", "spa-gp").First().IsValid);
            }
        }

        [TestMethod]
        public void DeleteGameData_AndClearAllData_RemoveExpectedRows()
        {
            using (var repository = CreateRepository())
            {
                repository.AddLap("AssettoCorsa", "BMW M3 GT2", "spa", "spa-gp", new RecordedLap
                {
                    LapNumber = 1,
                    LapTimeSeconds = 122.0,
                    Sector1Seconds = 40.0,
                    Sector2Seconds = 41.0,
                    Sector3Seconds = 41.0,
                    IsValid = true,
                    TimestampUtc = new DateTime(2026, 5, 25, 1, 0, 0, DateTimeKind.Utc)
                });
                repository.AddLap("Automobilista2", "BMW M4 GT4", "Watkins Glen", "Watkins_Glen-Watkins_Glen_GPIL", new RecordedLap
                {
                    LapNumber = 1,
                    LapTimeSeconds = 118.5,
                    Sector1Seconds = 31.5,
                    Sector2Seconds = 48.5,
                    Sector3Seconds = 38.5,
                    IsValid = true,
                    TimestampUtc = new DateTime(2026, 5, 25, 2, 0, 0, DateTimeKind.Utc)
                });

                repository.DeleteGameData("assetto corsa");

                var remainingSummaries = repository.GetTrackSummaries();
                Assert.AreEqual(1, remainingSummaries.Count);
                Assert.AreEqual("Automobilista2", remainingSummaries[0].GameName);
                Assert.AreEqual(0.0, repository.GetBestLapSeconds("AssettoCorsa", "BMW M3 GT2", "spa-gp"), 0.0001);

                repository.ClearAllData();

                Assert.IsFalse(repository.HasLapData());
                Assert.AreEqual(0, repository.GetTrackSummaries().Count);
            }
        }

        private StatsPlusSqliteRepository CreateRepository()
        {
            var repository = new StatsPlusSqliteRepository(_databasePath);
            repository.Initialize();
            return repository;
        }
    }
}
