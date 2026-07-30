using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;

namespace StatsPlus
{
    public sealed class StatsPlusLiteDbRepository : IDisposable
    {
        private const string TrackHistoriesCollectionName = "trackHistories";
        private const string LapsCollectionName = "laps";

        private static readonly BsonMapper Mapper = CreateMapper();

        private readonly string _databasePath;
        private LiteDatabase _database;

        private ILiteCollection<TrackHistoryDocument> TrackHistories => _database.GetCollection<TrackHistoryDocument>(TrackHistoriesCollectionName);
        private ILiteCollection<LapDocument> Laps => _database.GetCollection<LapDocument>(LapsCollectionName);

        public StatsPlusLiteDbRepository(string databasePath)
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

            _database = new LiteDatabase(_databasePath, Mapper);
            TrackHistories.EnsureIndex(history => history.NormalizedGameName);
            TrackHistories.EnsureIndex(history => history.NormalizedModelName);
            TrackHistories.EnsureIndex(history => history.NormalizedTrackNameWithConfig);
            Laps.EnsureIndex(lap => lap.TrackHistoryId);
            Laps.EnsureIndex(lap => lap.TimestampUtc);
            Laps.EnsureIndex(lap => lap.IsValid);
            Laps.EnsureIndex(lap => lap.LapTimeSeconds);
        }

        public bool HasLapData()
        {
            return Laps.Count() > 0;
        }

        public void AddLap(string gameName, string carModel, string trackName, string trackNameWithConfig, RecordedLap lap)
        {
            if (lap == null)
            {
                throw new ArgumentNullException(nameof(lap));
            }

            TrackHistoryDocument history = GetOrCreateTrackHistory(gameName, carModel, trackName, trackNameWithConfig, lap.TimestampUtc);
            Laps.Insert(new LapDocument
            {
                TrackHistoryId = history.Id,
                LapNumber = lap.LapNumber,
                LapTimeSeconds = lap.LapTimeSeconds,
                Sector1Seconds = lap.Sector1Seconds,
                Sector2Seconds = lap.Sector2Seconds,
                Sector3Seconds = lap.Sector3Seconds,
                IsValid = lap.IsValid,
                TimestampUtc = lap.TimestampUtc
            });
        }

        public void ToggleLapValidity(long lapId)
        {
            LapDocument lap = Laps.FindById(lapId);
            if (lap == null)
            {
                return;
            }

            lap.IsValid = !lap.IsValid;
            Laps.Update(lap);
        }

        public void DeleteGameData(string gameName)
        {
            List<TrackHistoryDocument> histories = TrackHistories.Find(history =>
                history.NormalizedGameName == NormalizeGameName(gameName)).ToList();

            foreach (TrackHistoryDocument history in histories)
            {
                Laps.DeleteMany(lap => lap.TrackHistoryId == history.Id);
                TrackHistories.Delete(history.Id);
            }
        }

        public void ClearAllData()
        {
            Laps.DeleteAll();
            TrackHistories.DeleteAll();
        }

        public double GetBestLapSeconds(string gameName, string carModel, string trackNameWithConfig)
        {
            TrackHistoryDocument history = FindTrackHistory(gameName, carModel, trackNameWithConfig);
            if (history == null)
            {
                return 0.0;
            }

            return Laps.Find(lap => lap.TrackHistoryId == history.Id && lap.IsValid && lap.LapTimeSeconds > 0)
                .Select(lap => lap.LapTimeSeconds)
                .DefaultIfEmpty(0.0)
                .Min();
        }

        public List<StoredTrackSummary> GetTrackSummaries()
        {
            var summaries = new List<StoredTrackSummary>();

            foreach (TrackHistoryDocument history in TrackHistories.FindAll())
            {
                List<LapDocument> laps = Laps.Find(lap => lap.TrackHistoryId == history.Id).ToList();
                summaries.Add(new StoredTrackSummary
                {
                    GameName = history.GameName,
                    CarModel = history.CarModel,
                    CarModelDisplay = history.DisplayModelName,
                    TrackName = history.RawTrackName,
                    TrackNameWithConfig = history.TrackNameWithConfig,
                    TrackNameWithConfigDisplay = history.DisplayTrackNameWithConfig,
                    LapCount = laps.Count,
                    BestLapSeconds = laps.Where(lap => lap.IsValid && lap.LapTimeSeconds > 0)
                        .Select(lap => lap.LapTimeSeconds)
                        .DefaultIfEmpty(0.0)
                        .Min(),
                    LastRecordedUtc = history.LastUpdatedUtc
                });
            }

            return summaries;
        }

        public List<RecordedLapView> GetTrackLaps(string gameName, string carModel, string trackNameWithConfig)
        {
            TrackHistoryDocument history = FindTrackHistory(gameName, carModel, trackNameWithConfig);
            if (history == null)
            {
                return new List<RecordedLapView>();
            }

            return Laps.Find(lap => lap.TrackHistoryId == history.Id)
                .OrderByDescending(lap => lap.TimestampUtc)
                .Select(lap => new RecordedLapView
                {
                    LapId = lap.Id,
                    GameName = history.GameName,
                    CarModel = history.CarModel,
                    CarModelDisplay = history.DisplayModelName,
                    TrackName = history.RawTrackName,
                    TrackNameWithConfig = history.TrackNameWithConfig,
                    TrackNameWithConfigDisplay = history.DisplayTrackNameWithConfig,
                    LapNumber = lap.LapNumber,
                    LapTimeSeconds = lap.LapTimeSeconds,
                    Sector1Seconds = lap.Sector1Seconds,
                    Sector2Seconds = lap.Sector2Seconds,
                    Sector3Seconds = lap.Sector3Seconds,
                    IsValid = lap.IsValid,
                    TimestampUtc = lap.TimestampUtc
                })
                .ToList();
        }

        public Dictionary<string, double> GetPersonalBestPropertyValues()
        {
            var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (StoredTrackSummary summary in GetTrackSummaries().Where(summary => summary.BestLapSeconds > 0))
            {
                values[StatsPlusPropertyNames.BuildPersonalBestPropertyName(
                    summary.GameName,
                    summary.CarModel,
                    summary.TrackNameWithConfig)] = summary.BestLapSeconds;
            }

            return values;
        }

        public string TryGetCarDisplayName(string gameName, string carModel)
        {
            TrackHistoryDocument history = TrackHistories.Find(history =>
                history.NormalizedGameName == NormalizeGameName(gameName) &&
                history.NormalizedModelName == NormalizeIdentityValue(carModel) &&
                !string.IsNullOrWhiteSpace(history.DisplayModelName)).FirstOrDefault();
            return history == null ? string.Empty : history.DisplayModelName;
        }

        public void Dispose()
        {
            _database?.Dispose();
            _database = null;
        }

        private TrackHistoryDocument GetOrCreateTrackHistory(string gameName, string carModel, string trackName, string trackNameWithConfig, DateTime timestampUtc)
        {
            TrackHistoryDocument history = FindTrackHistory(gameName, carModel, trackNameWithConfig);
            if (history != null)
            {
                if (timestampUtc > history.LastUpdatedUtc)
                {
                    history.LastUpdatedUtc = timestampUtc;
                    TrackHistories.Update(history);
                }

                return history;
            }

            history = new TrackHistoryDocument
            {
                GameName = gameName ?? string.Empty,
                NormalizedGameName = NormalizeGameName(gameName),
                CarModel = carModel ?? string.Empty,
                NormalizedModelName = NormalizeIdentityValue(carModel),
                RawTrackName = trackName ?? string.Empty,
                TrackNameWithConfig = trackNameWithConfig ?? string.Empty,
                NormalizedTrackNameWithConfig = NormalizeIdentityValue(trackNameWithConfig),
                CreatedUtc = timestampUtc,
                LastUpdatedUtc = timestampUtc
            };
            TrackHistories.Insert(history);
            return history;
        }

        private TrackHistoryDocument FindTrackHistory(string gameName, string carModel, string trackNameWithConfig)
        {
            string normalizedGameName = NormalizeGameName(gameName);
            string normalizedModelName = NormalizeIdentityValue(carModel);
            string normalizedTrackNameWithConfig = NormalizeIdentityValue(trackNameWithConfig);
            return TrackHistories.Find(history =>
                history.NormalizedGameName == normalizedGameName &&
                history.NormalizedModelName == normalizedModelName &&
                history.NormalizedTrackNameWithConfig == normalizedTrackNameWithConfig).FirstOrDefault();
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
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
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
}
