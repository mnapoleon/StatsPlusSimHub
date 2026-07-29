using System;

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
        public DateTime CreatedUtc { get; set; }
        public DateTime LastUpdatedUtc { get; set; }
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
        public DateTime TimestampUtc { get; set; }
    }
}
