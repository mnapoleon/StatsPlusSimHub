using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Data.SQLite;

namespace StatsPlus
{
    public sealed class StatsPlusSqliteRepository : IDisposable
    {
        private const string CreateSchemaSql = @"
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS games (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    normalized_name TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS cars (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    game_id INTEGER NOT NULL,
    model_name TEXT NOT NULL,
    normalized_model_name TEXT NOT NULL,
    display_model_name TEXT NULL,
    UNIQUE(game_id, normalized_model_name),
    FOREIGN KEY(game_id) REFERENCES games(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS tracks (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    game_id INTEGER NOT NULL,
    raw_track_name TEXT NOT NULL,
    track_name_with_config TEXT NOT NULL,
    normalized_track_name_with_config TEXT NOT NULL,
    display_track_name_with_config TEXT NULL,
    created_utc TEXT NOT NULL,
    last_updated_utc TEXT NOT NULL,
    UNIQUE(game_id, normalized_track_name_with_config),
    FOREIGN KEY(game_id) REFERENCES games(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS track_contexts (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    game_id INTEGER NOT NULL,
    car_id INTEGER NOT NULL,
    track_id INTEGER NOT NULL,
    created_utc TEXT NOT NULL,
    last_updated_utc TEXT NOT NULL,
    UNIQUE(game_id, car_id, track_id),
    FOREIGN KEY(game_id) REFERENCES games(id) ON DELETE CASCADE,
    FOREIGN KEY(car_id) REFERENCES cars(id) ON DELETE CASCADE,
    FOREIGN KEY(track_id) REFERENCES tracks(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS laps (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    track_context_id INTEGER NOT NULL,
    lap_number INTEGER NOT NULL,
    lap_time_seconds REAL NOT NULL,
    sector1_seconds REAL NOT NULL,
    sector2_seconds REAL NOT NULL,
    sector3_seconds REAL NOT NULL,
    is_valid INTEGER NOT NULL,
    timestamp_utc TEXT NOT NULL,
    FOREIGN KEY(track_context_id) REFERENCES track_contexts(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_cars_game_model
ON cars (game_id, normalized_model_name);

CREATE INDEX IF NOT EXISTS ix_tracks_game_track_config
ON tracks (game_id, normalized_track_name_with_config);

CREATE INDEX IF NOT EXISTS ix_track_contexts_lookup
ON track_contexts (game_id, car_id, track_id);

CREATE INDEX IF NOT EXISTS ix_laps_track_timestamp
ON laps (track_context_id, timestamp_utc DESC);

CREATE INDEX IF NOT EXISTS ix_laps_track_valid_laptime
ON laps (track_context_id, is_valid, lap_time_seconds);
";

        private readonly string _databasePath;
        private SQLiteConnection _connection;

        public StatsPlusSqliteRepository(string databasePath)
        {
            _databasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));
        }

        public void Initialize()
        {
            string directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _connection = new SQLiteConnection($"Data Source={_databasePath};Version=3;");
            _connection.Open();

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = CreateSchemaSql;
                command.ExecuteNonQuery();
            }
        }

        public bool HasLapData()
        {
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT EXISTS(SELECT 1 FROM laps LIMIT 1)";
                return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
            }
        }

        public void ImportLegacyDatabase(LapDatabase database)
        {
            if (database == null)
            {
                return;
            }

            using (var transaction = _connection.BeginTransaction())
            {
                foreach (KeyValuePair<string, GameBucket> gameEntry in database.Games)
                {
                    long gameId = GetOrCreateGameId(gameEntry.Key, transaction);

                    foreach (KeyValuePair<string, CarBucket> carEntry in gameEntry.Value.Cars)
                    {
                        long carId = GetOrCreateCarId(gameId, carEntry.Key, null, transaction);

                        foreach (KeyValuePair<string, TrackBucket> trackEntry in carEntry.Value.Tracks)
                        {
                            TrackBucket track = trackEntry.Value;
                            string trackNameWithConfig = string.IsNullOrWhiteSpace(track.TrackNameWithConfig)
                                ? trackEntry.Key
                                : track.TrackNameWithConfig;
                            long trackId = GetOrCreateTrackId(
                                gameId,
                                track.TrackName,
                                trackNameWithConfig,
                                null,
                                track.CreatedUtc,
                                track.LastUpdatedUtc,
                                transaction);
                            long trackContextId = GetOrCreateTrackContextId(
                                gameId,
                                carId,
                                trackId,
                                track.CreatedUtc,
                                track.LastUpdatedUtc,
                                transaction);

                            foreach (RecordedLap lap in track.Laps)
                            {
                                InsertLap(trackContextId, lap, transaction);
                            }
                        }
                    }
                }

                transaction.Commit();
            }
        }

        public void AddLap(string gameName, string carModel, string trackName, string trackNameWithConfig, RecordedLap lap)
        {
            DateTime timestampUtc = DateTime.UtcNow;

            using (var transaction = _connection.BeginTransaction())
            {
                long gameId = GetOrCreateGameId(gameName, transaction);
                long carId = GetOrCreateCarId(gameId, carModel, null, transaction);
                long trackId = GetOrCreateTrackId(gameId, trackName, trackNameWithConfig, null, timestampUtc, timestampUtc, transaction);
                long trackContextId = GetOrCreateTrackContextId(gameId, carId, trackId, timestampUtc, timestampUtc, transaction);

                UpdateTrackTimestamp(trackId, timestampUtc, transaction);
                UpdateTrackContextTimestamp(trackContextId, timestampUtc, transaction);
                InsertLap(trackContextId, lap, transaction);
                transaction.Commit();
            }
        }

        public void ToggleLapValidity(long lapId)
        {
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = @"
UPDATE laps
SET is_valid = CASE is_valid WHEN 1 THEN 0 ELSE 1 END
WHERE id = @lapId;";
                command.Parameters.AddWithValue("@lapId", lapId);
                command.ExecuteNonQuery();
            }
        }

        public void DeleteGameData(string gameName)
        {
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = @"
DELETE FROM games
WHERE normalized_name = @normalizedName;";
                command.Parameters.AddWithValue("@normalizedName", NormalizeGameName(gameName));
                command.ExecuteNonQuery();
            }
        }

        public void ClearAllData()
        {
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM games;";
                command.ExecuteNonQuery();
            }
        }

        public double GetBestLapSeconds(string gameName, string carModel, string trackNameWithConfig)
        {
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = @"
SELECT MIN(l.lap_time_seconds)
FROM laps l
INNER JOIN track_contexts tc ON tc.id = l.track_context_id
INNER JOIN games g ON g.id = tc.game_id
INNER JOIN cars c ON c.id = tc.car_id
INNER JOIN tracks t ON t.id = tc.track_id
WHERE g.normalized_name = @gameName
  AND c.normalized_model_name = @carModel
  AND t.normalized_track_name_with_config = @trackNameWithConfig
  AND l.is_valid = 1
  AND l.lap_time_seconds > 0;";
                command.Parameters.AddWithValue("@gameName", NormalizeGameName(gameName));
                command.Parameters.AddWithValue("@carModel", NormalizeIdentityValue(carModel));
                command.Parameters.AddWithValue("@trackNameWithConfig", NormalizeIdentityValue(trackNameWithConfig));

                object result = command.ExecuteScalar();
                return result == null || result is DBNull
                    ? 0.0
                    : Convert.ToDouble(result, CultureInfo.InvariantCulture);
            }
        }

        public List<StoredTrackSummary> GetTrackSummaries()
        {
            var summaries = new List<StoredTrackSummary>();

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = @"
SELECT
    g.name,
    c.model_name,
    c.display_model_name,
    t.raw_track_name,
    t.track_name_with_config,
    t.display_track_name_with_config,
    COUNT(l.id),
    MIN(CASE
        WHEN l.is_valid = 1 AND l.lap_time_seconds > 0 THEN l.lap_time_seconds
        ELSE NULL
    END),
    tc.last_updated_utc
FROM track_contexts tc
INNER JOIN games g ON g.id = tc.game_id
INNER JOIN cars c ON c.id = tc.car_id
INNER JOIN tracks t ON t.id = tc.track_id
LEFT JOIN laps l ON l.track_context_id = tc.id
GROUP BY
    g.name,
    c.model_name,
    c.display_model_name,
    t.raw_track_name,
    t.track_name_with_config,
    t.display_track_name_with_config,
    tc.last_updated_utc;";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        summaries.Add(new StoredTrackSummary
                        {
                            GameName = reader.GetString(0),
                            CarModel = reader.GetString(1),
                            CarModelDisplay = ReadNullableString(reader, 2),
                            TrackName = reader.GetString(3),
                            TrackNameWithConfig = reader.GetString(4),
                            TrackNameWithConfigDisplay = ReadNullableString(reader, 5),
                            LapCount = reader.GetInt32(6),
                            BestLapSeconds = reader.IsDBNull(7) ? 0.0 : reader.GetDouble(7),
                            LastRecordedUtc = ReadUtcDateTime(reader, 8)
                        });
                    }
                }
            }

            return summaries;
        }

        public List<RecordedLapView> GetTrackLaps(string gameName, string carModel, string trackNameWithConfig)
        {
            var laps = new List<RecordedLapView>();

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = @"
SELECT
    l.id,
    g.name,
    c.model_name,
    c.display_model_name,
    t.raw_track_name,
    t.track_name_with_config,
    t.display_track_name_with_config,
    l.lap_number,
    l.lap_time_seconds,
    l.sector1_seconds,
    l.sector2_seconds,
    l.sector3_seconds,
    l.is_valid,
    l.timestamp_utc
FROM laps l
INNER JOIN track_contexts tc ON tc.id = l.track_context_id
INNER JOIN games g ON g.id = tc.game_id
INNER JOIN cars c ON c.id = tc.car_id
INNER JOIN tracks t ON t.id = tc.track_id
WHERE g.normalized_name = @gameName
  AND c.normalized_model_name = @carModel
  AND t.normalized_track_name_with_config = @trackNameWithConfig
ORDER BY l.timestamp_utc DESC;";
                command.Parameters.AddWithValue("@gameName", NormalizeGameName(gameName));
                command.Parameters.AddWithValue("@carModel", NormalizeIdentityValue(carModel));
                command.Parameters.AddWithValue("@trackNameWithConfig", NormalizeIdentityValue(trackNameWithConfig));

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        laps.Add(new RecordedLapView
                        {
                            LapId = reader.GetInt64(0),
                            GameName = reader.GetString(1),
                            CarModel = reader.GetString(2),
                            CarModelDisplay = ReadNullableString(reader, 3),
                            TrackName = reader.GetString(4),
                            TrackNameWithConfig = reader.GetString(5),
                            TrackNameWithConfigDisplay = ReadNullableString(reader, 6),
                            LapNumber = reader.GetInt32(7),
                            LapTimeSeconds = reader.GetDouble(8),
                            Sector1Seconds = reader.GetDouble(9),
                            Sector2Seconds = reader.GetDouble(10),
                            Sector3Seconds = reader.GetDouble(11),
                            IsValid = reader.GetInt32(12) == 1,
                            TimestampUtc = ReadUtcDateTime(reader, 13)
                        });
                    }
                }
            }

            return laps;
        }

        public Dictionary<string, double> GetPersonalBestPropertyValues()
        {
            var personalBestValues = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = @"
SELECT
    g.name,
    c.model_name,
    t.track_name_with_config,
    MIN(l.lap_time_seconds)
FROM laps l
INNER JOIN track_contexts tc ON tc.id = l.track_context_id
INNER JOIN games g ON g.id = tc.game_id
INNER JOIN cars c ON c.id = tc.car_id
INNER JOIN tracks t ON t.id = tc.track_id
WHERE l.is_valid = 1
  AND l.lap_time_seconds > 0
GROUP BY g.name, c.model_name, t.track_name_with_config;";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string propertyName = StatsPlusPropertyNames.BuildPersonalBestPropertyName(
                            reader.GetString(0),
                            reader.GetString(1),
                            reader.GetString(2));
                        double bestLapSeconds = reader.GetDouble(3);
                        personalBestValues[propertyName] = bestLapSeconds;
                    }
                }
            }

            return personalBestValues;
        }

        public string TryGetCarDisplayName(string gameName, string carModel)
        {
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = @"
SELECT c.display_model_name
FROM cars c
INNER JOIN games g ON g.id = c.game_id
WHERE g.normalized_name = @gameName
  AND c.normalized_model_name = @carModel
LIMIT 1;";
                command.Parameters.AddWithValue("@gameName", NormalizeGameName(gameName));
                command.Parameters.AddWithValue("@carModel", NormalizeIdentityValue(carModel));

                object result = command.ExecuteScalar();
                return result == null || result is DBNull ? string.Empty : Convert.ToString(result, CultureInfo.InvariantCulture);
            }
        }

        public void Dispose()
        {
            _connection?.Dispose();
            _connection = null;
        }

        private long GetOrCreateGameId(string gameName, SQLiteTransaction transaction)
        {
            string normalizedName = NormalizeGameName(gameName);

            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO games (name, normalized_name)
VALUES (@name, @normalizedName)
ON CONFLICT(normalized_name) DO UPDATE SET name = COALESCE(games.name, excluded.name);

SELECT id
FROM games
WHERE normalized_name = @normalizedName;";
                command.Parameters.AddWithValue("@name", gameName);
                command.Parameters.AddWithValue("@normalizedName", normalizedName);
                return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        private long GetOrCreateCarId(long gameId, string carModel, string displayModelName, SQLiteTransaction transaction)
        {
            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO cars (game_id, model_name, normalized_model_name, display_model_name)
VALUES (@gameId, @modelName, @normalizedModelName, @displayModelName)
ON CONFLICT(game_id, normalized_model_name) DO UPDATE SET
    model_name = excluded.model_name,
    display_model_name = COALESCE(cars.display_model_name, excluded.display_model_name);

SELECT id
FROM cars
WHERE game_id = @gameId
  AND normalized_model_name = @normalizedModelName;";
                command.Parameters.AddWithValue("@gameId", gameId);
                command.Parameters.AddWithValue("@modelName", carModel);
                command.Parameters.AddWithValue("@normalizedModelName", NormalizeIdentityValue(carModel));
                command.Parameters.AddWithValue("@displayModelName", ToDbValue(displayModelName));
                return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        private long GetOrCreateTrackId(
            long gameId,
            string rawTrackName,
            string trackNameWithConfig,
            string displayTrackNameWithConfig,
            DateTime createdUtc,
            DateTime lastUpdatedUtc,
            SQLiteTransaction transaction)
        {
            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO tracks (
    game_id,
    raw_track_name,
    track_name_with_config,
    normalized_track_name_with_config,
    display_track_name_with_config,
    created_utc,
    last_updated_utc)
VALUES (
    @gameId,
    @rawTrackName,
    @trackNameWithConfig,
    @normalizedTrackNameWithConfig,
    @displayTrackNameWithConfig,
    @createdUtc,
    @lastUpdatedUtc)
ON CONFLICT(game_id, normalized_track_name_with_config) DO UPDATE SET
    raw_track_name = excluded.raw_track_name,
    track_name_with_config = excluded.track_name_with_config,
    display_track_name_with_config = COALESCE(tracks.display_track_name_with_config, excluded.display_track_name_with_config),
    created_utc = MIN(tracks.created_utc, excluded.created_utc),
    last_updated_utc = MAX(tracks.last_updated_utc, excluded.last_updated_utc);

SELECT id
FROM tracks
WHERE game_id = @gameId
  AND normalized_track_name_with_config = @normalizedTrackNameWithConfig;";
                command.Parameters.AddWithValue("@gameId", gameId);
                command.Parameters.AddWithValue("@rawTrackName", rawTrackName);
                command.Parameters.AddWithValue("@trackNameWithConfig", trackNameWithConfig);
                command.Parameters.AddWithValue("@normalizedTrackNameWithConfig", NormalizeIdentityValue(trackNameWithConfig));
                command.Parameters.AddWithValue("@displayTrackNameWithConfig", ToDbValue(displayTrackNameWithConfig));
                command.Parameters.AddWithValue("@createdUtc", ToUtcString(createdUtc));
                command.Parameters.AddWithValue("@lastUpdatedUtc", ToUtcString(lastUpdatedUtc));
                return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        private long GetOrCreateTrackContextId(
            long gameId,
            long carId,
            long trackId,
            DateTime createdUtc,
            DateTime lastUpdatedUtc,
            SQLiteTransaction transaction)
        {
            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO track_contexts (game_id, car_id, track_id, created_utc, last_updated_utc)
VALUES (@gameId, @carId, @trackId, @createdUtc, @lastUpdatedUtc)
ON CONFLICT(game_id, car_id, track_id) DO UPDATE SET
    created_utc = MIN(track_contexts.created_utc, excluded.created_utc),
    last_updated_utc = MAX(track_contexts.last_updated_utc, excluded.last_updated_utc);

SELECT id
FROM track_contexts
WHERE game_id = @gameId
  AND car_id = @carId
  AND track_id = @trackId;";
                command.Parameters.AddWithValue("@gameId", gameId);
                command.Parameters.AddWithValue("@carId", carId);
                command.Parameters.AddWithValue("@trackId", trackId);
                command.Parameters.AddWithValue("@createdUtc", ToUtcString(createdUtc));
                command.Parameters.AddWithValue("@lastUpdatedUtc", ToUtcString(lastUpdatedUtc));
                return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        private void UpdateTrackTimestamp(long trackId, DateTime lastUpdatedUtc, SQLiteTransaction transaction)
        {
            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
UPDATE tracks
SET last_updated_utc = @lastUpdatedUtc
WHERE id = @trackId;";
                command.Parameters.AddWithValue("@lastUpdatedUtc", ToUtcString(lastUpdatedUtc));
                command.Parameters.AddWithValue("@trackId", trackId);
                command.ExecuteNonQuery();
            }
        }

        private void UpdateTrackContextTimestamp(long trackContextId, DateTime lastUpdatedUtc, SQLiteTransaction transaction)
        {
            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
UPDATE track_contexts
SET last_updated_utc = @lastUpdatedUtc
WHERE id = @trackContextId;";
                command.Parameters.AddWithValue("@lastUpdatedUtc", ToUtcString(lastUpdatedUtc));
                command.Parameters.AddWithValue("@trackContextId", trackContextId);
                command.ExecuteNonQuery();
            }
        }

        private void InsertLap(long trackContextId, RecordedLap lap, SQLiteTransaction transaction)
        {
            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO laps (
    track_context_id,
    lap_number,
    lap_time_seconds,
    sector1_seconds,
    sector2_seconds,
    sector3_seconds,
    is_valid,
    timestamp_utc)
VALUES (
    @trackContextId,
    @lapNumber,
    @lapTimeSeconds,
    @sector1Seconds,
    @sector2Seconds,
    @sector3Seconds,
    @isValid,
    @timestampUtc);";
                command.Parameters.AddWithValue("@trackContextId", trackContextId);
                command.Parameters.AddWithValue("@lapNumber", lap.LapNumber);
                command.Parameters.AddWithValue("@lapTimeSeconds", lap.LapTimeSeconds);
                command.Parameters.AddWithValue("@sector1Seconds", lap.Sector1Seconds);
                command.Parameters.AddWithValue("@sector2Seconds", lap.Sector2Seconds);
                command.Parameters.AddWithValue("@sector3Seconds", lap.Sector3Seconds);
                command.Parameters.AddWithValue("@isValid", lap.IsValid ? 1 : 0);
                command.Parameters.AddWithValue("@timestampUtc", ToUtcString(lap.TimestampUtc));
                command.ExecuteNonQuery();
            }
        }

        private static string NormalizeGameName(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
            {
                return string.Empty;
            }

            var characters = new List<char>(gameName.Length);
            foreach (char character in gameName)
            {
                if (char.IsLetterOrDigit(character))
                {
                    characters.Add(char.ToLowerInvariant(character));
                }
            }

            return new string(characters.ToArray());
        }

        private static string NormalizeIdentityValue(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToUpperInvariant();
        }

        private static string ToUtcString(DateTime value)
        {
            DateTime utc = value.Kind == DateTimeKind.Utc
                ? value
                : value.ToUniversalTime();
            return utc.ToString("o", CultureInfo.InvariantCulture);
        }

        private static DateTime ReadUtcDateTime(SQLiteDataReader reader, int ordinal)
        {
            string value = reader.GetString(ordinal);
            return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        private static string ReadNullableString(SQLiteDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
        }

        private static object ToDbValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? (object)DBNull.Value : value;
        }
    }
}
