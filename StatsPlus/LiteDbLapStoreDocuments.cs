using System;
using LiteDB;

namespace StatsPlus
{
    public sealed class TrackHistoryDocument
    {
        public long Id { get; set; }
        public string GameName { get; set; } = string.Empty;
        public string NormalizedGameName { get; set; } = string.Empty;
        public string CarModel { get; set; } = string.Empty;
        public string NormalizedModelName { get; set; } = string.Empty;
        public string DisplayModelName { get; set; } = string.Empty;
        public string RawTrackName { get; set; } = string.Empty;
        public string TrackNameWithConfig { get; set; } = string.Empty;
        public string NormalizedTrackNameWithConfig { get; set; } = string.Empty;
        public string DisplayTrackNameWithConfig { get; set; } = string.Empty;

        [BsonField("CreatedUtc")]
        public long CreatedUtcTicks { get; set; }

        [BsonField("LastUpdatedUtc")]
        public long LastUpdatedUtcTicks { get; set; }

        [BsonIgnore]
        public DateTime CreatedUtc
        {
            get => new DateTime(CreatedUtcTicks, DateTimeKind.Utc);
            set => CreatedUtcTicks = ToUtcTicks(value);
        }

        [BsonIgnore]
        public DateTime LastUpdatedUtc
        {
            get => new DateTime(LastUpdatedUtcTicks, DateTimeKind.Utc);
            set => LastUpdatedUtcTicks = ToUtcTicks(value);
        }

        private static long ToUtcTicks(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc
                ? value.Ticks
                : value.ToUniversalTime().Ticks;
        }
    }

    public sealed class LapDocument
    {
        public long Id { get; set; }
        public long TrackHistoryId { get; set; }
        public int LapNumber { get; set; }
        public double LapTimeSeconds { get; set; }
        public double Sector1Seconds { get; set; }
        public double Sector2Seconds { get; set; }
        public double Sector3Seconds { get; set; }
        public bool IsValid { get; set; }

        [BsonField("TimestampUtc")]
        public long TimestampUtcTicks { get; set; }

        [BsonIgnore]
        public DateTime TimestampUtc
        {
            get => new DateTime(TimestampUtcTicks, DateTimeKind.Utc);
            set => TimestampUtcTicks = ToUtcTicks(value);
        }

        private static long ToUtcTicks(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc
                ? value.Ticks
                : value.ToUniversalTime().Ticks;
        }
    }
}
