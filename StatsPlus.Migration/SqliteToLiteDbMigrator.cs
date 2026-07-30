using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using LiteDB;
using StatsPlus;

namespace StatsPlus.Migration
{
    public sealed class SqliteToLiteDbMigrator
    {
        private const string TrackHistoriesCollectionName = "trackHistories";
        private const string LapsCollectionName = "laps";

        private const string TrackContextsQuery = @"
SELECT
    tc.id,
    g.name,
    g.normalized_name,
    c.model_name,
    c.normalized_model_name,
    c.display_model_name,
    t.raw_track_name,
    t.track_name_with_config,
    t.normalized_track_name_with_config,
    t.display_track_name_with_config,
    tc.created_utc,
    tc.last_updated_utc
FROM track_contexts tc
INNER JOIN games g ON g.id = tc.game_id
INNER JOIN cars c ON c.id = tc.car_id
INNER JOIN tracks t ON t.id = tc.track_id;";

        private const string LapsQuery = @"
SELECT
    id,
    track_context_id,
    lap_number,
    lap_time_seconds,
    sector1_seconds,
    sector2_seconds,
    sector3_seconds,
    is_valid,
    timestamp_utc
FROM laps;";

        public MigrationResult Migrate(string sourcePath, string targetPath, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new ArgumentException("A source database path is required.", nameof(sourcePath));
            }

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("The source SQLite database was not found.", sourcePath);
            }

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                throw new ArgumentException("A target database path is required.", nameof(targetPath));
            }

            if (File.Exists(targetPath) && !overwrite)
            {
                throw new IOException("The target LiteDB database already exists.");
            }

            string targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            string temporaryPath = targetPath + ".tmp";
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            try
            {
                MigrationResult result = WriteTemporaryDatabase(sourcePath, temporaryPath);
                PromoteTemporaryDatabase(temporaryPath, targetPath, overwrite);
                return result;
            }
            catch
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }

                throw;
            }
        }

        private static MigrationResult WriteTemporaryDatabase(string sourcePath, string temporaryPath)
        {
            var contextIds = new Dictionary<long, long>();
            int trackHistoryCount = 0;
            int lapCount = 0;

            using (var source = new SQLiteConnection($"Data Source={sourcePath};Version=3;Read Only=True;"))
            using (var target = new LiteDatabase(temporaryPath, CreateMapper()))
            {
                source.Open();
                ILiteCollection<TrackHistoryDocument> trackHistories = target.GetCollection<TrackHistoryDocument>(TrackHistoriesCollectionName);
                ILiteCollection<LapDocument> laps = target.GetCollection<LapDocument>(LapsCollectionName);

                using (var command = new SQLiteCommand(TrackContextsQuery, source))
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        long sqliteTrackContextId = reader.GetInt64(0);
                        var history = new TrackHistoryDocument
                        {
                            GameName = reader.GetString(1),
                            NormalizedGameName = reader.GetString(2),
                            CarModel = reader.GetString(3),
                            NormalizedModelName = reader.GetString(4),
                            DisplayModelName = ReadNullableString(reader, 5),
                            RawTrackName = reader.GetString(6),
                            TrackNameWithConfig = reader.GetString(7),
                            NormalizedTrackNameWithConfig = reader.GetString(8),
                            DisplayTrackNameWithConfig = ReadNullableString(reader, 9),
                            CreatedUtc = ReadUtcDateTime(reader, 10),
                            LastUpdatedUtc = ReadUtcDateTime(reader, 11)
                        };

                        trackHistories.Insert(history);
                        contextIds.Add(sqliteTrackContextId, history.Id);
                        trackHistoryCount++;
                    }
                }

                using (var command = new SQLiteCommand(LapsQuery, source))
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        long sqliteTrackContextId = reader.GetInt64(1);
                        if (!contextIds.TryGetValue(sqliteTrackContextId, out long trackHistoryId))
                        {
                            throw new InvalidDataException($"Lap {reader.GetInt64(0)} references an unknown track context {sqliteTrackContextId}.");
                        }

                        laps.Insert(new LapDocument
                        {
                            Id = reader.GetInt64(0),
                            TrackHistoryId = trackHistoryId,
                            LapNumber = reader.GetInt32(2),
                            LapTimeSeconds = reader.GetDouble(3),
                            Sector1Seconds = reader.GetDouble(4),
                            Sector2Seconds = reader.GetDouble(5),
                            Sector3Seconds = reader.GetDouble(6),
                            IsValid = reader.GetInt32(7) == 1,
                            TimestampUtc = ReadUtcDateTime(reader, 8)
                        });
                        lapCount++;
                    }
                }
            }

            return new MigrationResult(trackHistoryCount, lapCount);
        }

        private static void PromoteTemporaryDatabase(string temporaryPath, string targetPath, bool overwrite)
        {
            if (File.Exists(targetPath) && overwrite)
            {
                File.Replace(temporaryPath, targetPath, null);
                return;
            }

            File.Move(temporaryPath, targetPath);
        }

        private static string ReadNullableString(SQLiteDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
        }

        private static DateTime ReadUtcDateTime(SQLiteDataReader reader, int ordinal)
        {
            return DateTime.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        private static BsonMapper CreateMapper()
        {
            BsonMapper mapper = BsonMapper.Global;
            mapper.RegisterType<DateTime>(
                value => new BsonValue(value.ToUniversalTime()),
                value => value.AsDateTime.ToUniversalTime());
            return mapper;
        }
    }

    public sealed class MigrationResult
    {
        public MigrationResult(int trackHistoryCount, int lapCount)
        {
            TrackHistoryCount = trackHistoryCount;
            LapCount = lapCount;
        }

        public int TrackHistoryCount { get; }
        public int LapCount { get; }
    }
}
