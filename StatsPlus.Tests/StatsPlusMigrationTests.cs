using System;
using System.IO;
using System.Linq;
using System.Data.SQLite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StatsPlus.Migration;

namespace StatsPlus.Tests
{
    [TestClass]
    public class StatsPlusMigrationTests
    {
        private string _tempDirectory;
        private string _sourcePath;
        private string _targetPath;

        [TestInitialize]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "StatsPlusMigrationTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
            _sourcePath = Path.Combine(_tempDirectory, "StatsPlus.laps.db");
            _targetPath = Path.Combine(_tempDirectory, "StatsPlus.laps.ldb");
            CreateSourceDatabase(_sourcePath);
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
        public void Migrate_CollapsesTrackContextsAndPreservesLapHistory()
        {
            var expectedSpaTimestamp = new DateTime(2026, 7, 1, 12, 30, 0, DateTimeKind.Utc);
            var expectedMonzaTimestamp = new DateTime(2026, 7, 2, 13, 45, 0, DateTimeKind.Utc);

            var result = new SqliteToLiteDbMigrator().Migrate(_sourcePath, _targetPath, overwrite: false);

            Assert.AreEqual(2, result.TrackHistoryCount);
            Assert.AreEqual(3, result.LapCount);

            using (var repository = OpenTargetRepository())
            {
                var summaries = repository.GetTrackSummaries();
                Assert.AreEqual(2, summaries.Count);
                Assert.AreEqual(2, summaries.Single(summary => summary.TrackNameWithConfig == "spa-gp").LapCount);
                Assert.AreEqual(100.25, repository.GetBestLapSeconds("AssettoCorsa", "BMW M3 GT2", "spa-gp"), 0.0001);
                Assert.AreEqual(89.5, repository.GetBestLapSeconds("Automobilista2", "BMW M4 GT4", "monza-gp"), 0.0001);

                var spaLaps = repository.GetTrackLaps("AssettoCorsa", "BMW M3 GT2", "spa-gp").OrderBy(lap => lap.LapId).ToList();
                Assert.AreEqual(2, spaLaps.Count);
                Assert.IsTrue(spaLaps[0].IsValid);
                Assert.IsFalse(spaLaps[1].IsValid);
                Assert.AreEqual(expectedSpaTimestamp, spaLaps[1].TimestampUtc);

                var monzaLap = repository.GetTrackLaps("Automobilista2", "BMW M4 GT4", "monza-gp").Single();
                Assert.IsTrue(monzaLap.IsValid);
                Assert.AreEqual(expectedMonzaTimestamp, monzaLap.TimestampUtc);

                var personalBests = repository.GetPersonalBestPropertyValues();
                Assert.AreEqual(2, personalBests.Count);
                Assert.AreEqual(100.25, personalBests["StatsPlus.PersonalBest.AssettoCorsa.BMW_M3_GT2.spa_gp"], 0.0001);
                Assert.AreEqual(89.5, personalBests["StatsPlus.PersonalBest.Automobilista2.BMW_M4_GT4.monza_gp"], 0.0001);
            }
        }

        [TestMethod]
        public void Migrate_ExistingTargetWithoutOverwrite_ThrowsIOException()
        {
            File.WriteAllText(_targetPath, "existing target");

            Assert.ThrowsException<IOException>(() => new SqliteToLiteDbMigrator().Migrate(_sourcePath, _targetPath, overwrite: false));
        }

        [TestMethod]
        public void Migrate_UsesNewestLapTimestampForLastUpdatedUtc()
        {
            var expectedLatestLapTimestamp = new DateTime(2026, 7, 1, 12, 30, 0, DateTimeKind.Utc);

            new SqliteToLiteDbMigrator().Migrate(_sourcePath, _targetPath, overwrite: false);

            using (var repository = OpenTargetRepository())
            {
                var spaSummary = repository.GetTrackSummaries().Single(summary => summary.TrackNameWithConfig == "spa-gp");
                Assert.AreEqual(expectedLatestLapTimestamp, spaSummary.LastRecordedUtc);
            }
        }

        [TestMethod]
        public void Migrate_WhenSourceAndTargetAreTheSame_ThrowsAndLeavesSourceUntouched()
        {
            byte[] originalSourceBytes = File.ReadAllBytes(_sourcePath);

            Assert.ThrowsException<IOException>(() => new SqliteToLiteDbMigrator().Migrate(_sourcePath, _sourcePath, overwrite: true));

            CollectionAssert.AreEqual(originalSourceBytes, File.ReadAllBytes(_sourcePath));
        }

        [TestMethod]
        public void Migrate_WhenSourceIsTemporaryTarget_ThrowsAndLeavesSourceUntouched()
        {
            string temporarySourcePath = _targetPath + ".tmp";
            File.Move(_sourcePath, temporarySourcePath);
            byte[] originalSourceBytes = File.ReadAllBytes(temporarySourcePath);

            Assert.ThrowsException<IOException>(() => new SqliteToLiteDbMigrator().Migrate(temporarySourcePath, _targetPath, overwrite: true));

            CollectionAssert.AreEqual(originalSourceBytes, File.ReadAllBytes(temporarySourcePath));
        }

        [TestMethod]
        public void Migrate_LeavesSourceDatabaseUntouched()
        {
            byte[] originalSourceBytes = File.ReadAllBytes(_sourcePath);

            new SqliteToLiteDbMigrator().Migrate(_sourcePath, _targetPath, overwrite: false);

            CollectionAssert.AreEqual(originalSourceBytes, File.ReadAllBytes(_sourcePath));
        }

        private StatsPlusLiteDbRepository OpenTargetRepository()
        {
            var repository = new StatsPlusLiteDbRepository(_targetPath);
            repository.Initialize();
            return repository;
        }

        private static void CreateSourceDatabase(string databasePath)
        {
            using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
            {
                connection.Open();
                Execute(connection, @"
CREATE TABLE games (id INTEGER PRIMARY KEY, name TEXT NOT NULL, normalized_name TEXT NOT NULL);
CREATE TABLE cars (id INTEGER PRIMARY KEY, game_id INTEGER NOT NULL, model_name TEXT NOT NULL, normalized_model_name TEXT NOT NULL, display_model_name TEXT NULL);
CREATE TABLE tracks (id INTEGER PRIMARY KEY, game_id INTEGER NOT NULL, raw_track_name TEXT NOT NULL, track_name_with_config TEXT NOT NULL, normalized_track_name_with_config TEXT NOT NULL, display_track_name_with_config TEXT NULL, created_utc TEXT NOT NULL, last_updated_utc TEXT NOT NULL);
CREATE TABLE track_contexts (id INTEGER PRIMARY KEY, game_id INTEGER NOT NULL, car_id INTEGER NOT NULL, track_id INTEGER NOT NULL, created_utc TEXT NOT NULL, last_updated_utc TEXT NOT NULL);
CREATE TABLE laps (id INTEGER PRIMARY KEY, track_context_id INTEGER NOT NULL, lap_number INTEGER NOT NULL, lap_time_seconds REAL NOT NULL, sector1_seconds REAL NOT NULL, sector2_seconds REAL NOT NULL, sector3_seconds REAL NOT NULL, is_valid INTEGER NOT NULL, timestamp_utc TEXT NOT NULL);");

                Execute(connection, "INSERT INTO games VALUES (1, 'AssettoCorsa', 'assettocorsa'); INSERT INTO games VALUES (2, 'Automobilista2', 'automobilista2');");
                Execute(connection, "INSERT INTO cars VALUES (1, 1, 'BMW M3 GT2', 'BMW M3 GT2', 'BMW M3 GT2 Race'); INSERT INTO cars VALUES (2, 2, 'BMW M4 GT4', 'BMW M4 GT4', 'BMW M4 GT4 Race');");
                Execute(connection, "INSERT INTO tracks VALUES (1, 1, 'spa', 'spa-gp', 'SPA-GP', 'Spa GP', '2026-07-01T12:00:00.0000000Z', '2026-07-01T12:30:00.0000000Z'); INSERT INTO tracks VALUES (2, 2, 'monza', 'monza-gp', 'MONZA-GP', 'Monza GP', '2026-07-02T13:00:00.0000000Z', '2026-07-02T13:45:00.0000000Z');");
                Execute(connection, "INSERT INTO track_contexts VALUES (1, 1, 1, 1, '2026-07-01T12:00:00.0000000Z', '2026-07-01T12:05:00.0000000Z'); INSERT INTO track_contexts VALUES (2, 2, 2, 2, '2026-07-02T13:00:00.0000000Z', '2026-07-02T13:45:00.0000000Z');");
                Execute(connection, "INSERT INTO laps VALUES (11, 1, 1, 100.25, 33.0, 33.5, 33.75, 1, '2026-07-01T12:15:00.0000000Z'); INSERT INTO laps VALUES (12, 1, 2, 99.5, 32.5, 33.0, 34.0, 0, '2026-07-01T12:30:00.0000000Z'); INSERT INTO laps VALUES (21, 2, 1, 89.5, 29.5, 30.0, 30.0, 1, '2026-07-02T13:45:00.0000000Z');");
            }
        }

        private static void Execute(SQLiteConnection connection, string commandText)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = commandText;
                command.ExecuteNonQuery();
            }
        }
    }
}
