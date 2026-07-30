using System;
using System.IO;
using System.Linq;
using LiteDB;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace StatsPlus.Tests
{
    [TestClass]
    public class StatsPlusLiteDbRepositoryTests
    {
        private string _tempDirectory;
        private string _databasePath;

        [TestInitialize]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "StatsPlusLiteDbTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
            _databasePath = Path.Combine(_tempDirectory, "StatsPlus.laps.ldb");
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
        public void Initialize_CreatesEmptyStore()
        {
            using (var repository = CreateRepository())
            {
                Assert.IsFalse(repository.HasLapData());
                Assert.AreEqual(0, repository.GetTrackSummaries().Count);
            }
        }

        [TestMethod]
        public void AddLap_UsesCaseInsensitiveIdentity_AndPreservesCreatedUtc()
        {
            var firstTimestamp = new DateTime(2026, 5, 25, 1, 0, 0, DateTimeKind.Utc);
            var newerTimestamp = new DateTime(2026, 5, 25, 2, 0, 0, DateTimeKind.Utc);

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
                    TimestampUtc = firstTimestamp
                });

                repository.AddLap("assettocorsa", "bmw m3 gt2", "spa", "SPA-GP", new RecordedLap
                {
                    LapNumber = 2,
                    LapTimeSeconds = 119.0,
                    Sector1Seconds = 39.0,
                    Sector2Seconds = 40.0,
                    Sector3Seconds = 40.0,
                    IsValid = true,
                    TimestampUtc = newerTimestamp
                });
            }

            using (var database = new LiteDatabase(_databasePath))
            {
                var histories = database.GetCollection<TrackHistoryDocument>("trackHistories").FindAll().ToList();
                Assert.AreEqual(1, histories.Count);
                Assert.AreEqual(firstTimestamp, histories[0].CreatedUtc.ToUniversalTime());
                Assert.AreEqual(newerTimestamp, histories[0].LastUpdatedUtc.ToUniversalTime());
            }
        }

        [TestMethod]
        public void AddLap_IgnoresOlderLapWhenUpdatingLastUpdatedUtc()
        {
            var newerTimestamp = new DateTime(2026, 5, 25, 2, 0, 0, DateTimeKind.Utc);
            var olderTimestamp = new DateTime(2026, 5, 25, 1, 0, 0, DateTimeKind.Utc);

            using (var repository = CreateRepository())
            {
                repository.AddLap("AssettoCorsa", "BMW M3 GT2", "spa", "spa-gp", new RecordedLap { LapNumber = 2, LapTimeSeconds = 119.0, IsValid = true, TimestampUtc = newerTimestamp });
                repository.AddLap("AssettoCorsa", "BMW M3 GT2", "spa", "spa-gp", new RecordedLap { LapNumber = 1, LapTimeSeconds = 122.0, IsValid = true, TimestampUtc = olderTimestamp });
            }

            using (var database = new LiteDatabase(_databasePath))
            {
                var history = database.GetCollection<TrackHistoryDocument>("trackHistories").FindAll().Single();
                Assert.AreEqual(newerTimestamp, history.LastUpdatedUtc.ToUniversalTime());
            }
        }

        [TestMethod]
        public void ToggleLapValidity_RecalculatesBestLap()
        {
            using (var repository = CreateRepository())
            {
                repository.AddLap("AssettoCorsa", "BMW M3 GT2", "spa", "spa-gp", new RecordedLap { LapNumber = 1, LapTimeSeconds = 122.0, IsValid = true, TimestampUtc = new DateTime(2026, 5, 25, 1, 0, 0, DateTimeKind.Utc) });
                repository.AddLap("AssettoCorsa", "BMW M3 GT2", "spa", "spa-gp", new RecordedLap { LapNumber = 2, LapTimeSeconds = 119.0, IsValid = true, TimestampUtc = new DateTime(2026, 5, 25, 1, 2, 0, DateTimeKind.Utc) });

                var laps = repository.GetTrackLaps("AssettoCorsa", "BMW M3 GT2", "spa-gp");
                repository.ToggleLapValidity(laps[0].LapId);

                Assert.AreEqual(122.0, repository.GetBestLapSeconds("AssettoCorsa", "BMW M3 GT2", "SPA-GP"), 0.0001);
                Assert.AreEqual(122.0, repository.GetTrackSummaries().Single().BestLapSeconds, 0.0001);
                Assert.IsFalse(repository.GetTrackLaps("AssettoCorsa", "BMW M3 GT2", "spa-gp").First().IsValid);
            }
        }

        [TestMethod]
        public void DeleteGameData_AndClearAllData_RemoveExpectedDocuments()
        {
            using (var repository = CreateRepository())
            {
                repository.AddLap("AssettoCorsa", "BMW M3 GT2", "spa", "spa-gp", new RecordedLap { LapNumber = 1, LapTimeSeconds = 122.0, IsValid = true, TimestampUtc = new DateTime(2026, 5, 25, 1, 0, 0, DateTimeKind.Utc) });
                repository.AddLap("Automobilista2", "BMW M4 GT4", "Watkins Glen", "Watkins_Glen-Watkins_Glen_GPIL", new RecordedLap { LapNumber = 1, LapTimeSeconds = 118.5, IsValid = true, TimestampUtc = new DateTime(2026, 5, 25, 2, 0, 0, DateTimeKind.Utc) });

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

        [TestMethod]
        public void Queries_ReturnSummariesLapsAndPersonalBests()
        {
            var newestLapTime = new DateTime(2026, 5, 25, 1, 4, 0, DateTimeKind.Utc);
            var olderLapTime = new DateTime(2026, 5, 25, 1, 2, 0, DateTimeKind.Utc);

            using (var repository = CreateRepository())
            {
                repository.AddLap("AssettoCorsa", "BMW M3 GT2", "spa", "spa-gp", new RecordedLap { LapNumber = 1, LapTimeSeconds = 121.5, Sector1Seconds = 40.1, Sector2Seconds = 40.5, Sector3Seconds = 40.9, IsValid = true, TimestampUtc = olderLapTime });
                repository.AddLap("AssettoCorsa", "BMW M3 GT2", "spa", "spa-gp", new RecordedLap { LapNumber = 2, LapTimeSeconds = 119.25, Sector1Seconds = 39.7, Sector2Seconds = 39.8, Sector3Seconds = 39.75, IsValid = false, TimestampUtc = newestLapTime });

                var summaries = repository.GetTrackSummaries();
                Assert.AreEqual(1, summaries.Count);
                Assert.AreEqual("AssettoCorsa", summaries[0].GameName);
                Assert.AreEqual("BMW M3 GT2", summaries[0].CarModel);
                Assert.AreEqual("spa-gp", summaries[0].TrackNameWithConfig);
                Assert.AreEqual(2, summaries[0].LapCount);
                Assert.AreEqual(121.5, summaries[0].BestLapSeconds, 0.0001);
                Assert.AreEqual(newestLapTime, summaries[0].LastRecordedUtc);

                var laps = repository.GetTrackLaps("assettocorsa", "bmw m3 gt2", "SPA-GP");
                Assert.AreEqual(2, laps.Count);
                Assert.AreEqual(2, laps[0].LapNumber);
                Assert.AreEqual(newestLapTime, laps[0].TimestampUtc);
                Assert.AreEqual(1, laps[1].LapNumber);

                var personalBests = repository.GetPersonalBestPropertyValues();
                Assert.AreEqual(1, personalBests.Count);
                Assert.AreEqual(121.5, personalBests["StatsPlus.PersonalBest.AssettoCorsa.BMW_M3_GT2.spa_gp"], 0.0001);
            }
        }

        private StatsPlusLiteDbRepository CreateRepository()
        {
            var repository = new StatsPlusLiteDbRepository(_databasePath);
            repository.Initialize();
            return repository;
        }
    }
}
