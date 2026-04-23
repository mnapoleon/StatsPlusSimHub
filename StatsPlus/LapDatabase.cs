using System;
using System.Collections.Generic;

namespace StatsPlus
{
    public class LapDatabase
    {
        public Dictionary<string, GameBucket> Games { get; set; } = new Dictionary<string, GameBucket>(StringComparer.OrdinalIgnoreCase);
    }

    public class GameBucket
    {
        public Dictionary<string, CarBucket> Cars { get; set; } = new Dictionary<string, CarBucket>(StringComparer.OrdinalIgnoreCase);
    }

    public class CarBucket
    {
        public Dictionary<string, TrackBucket> Tracks { get; set; } = new Dictionary<string, TrackBucket>(StringComparer.OrdinalIgnoreCase);
    }

    public class TrackBucket
    {
        public string GameName { get; set; } = string.Empty;

        public string CarModel { get; set; } = string.Empty;

        public string TrackName { get; set; } = string.Empty;

        public string TrackNameWithConfig { get; set; } = string.Empty;

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;

        public List<RecordedLap> Laps { get; set; } = new List<RecordedLap>();
    }

    public class RecordedLap
    {
        public int LapNumber { get; set; }

        public double LapTimeSeconds { get; set; }

        public double Sector1Seconds { get; set; }

        public double Sector2Seconds { get; set; }

        public double Sector3Seconds { get; set; }

        public bool IsValid { get; set; }

        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    }

    public class StoredTrackSummary
    {
        public string GameName { get; set; } = string.Empty;

        public string CarModel { get; set; } = string.Empty;

        public string TrackName { get; set; } = string.Empty;

        public string TrackNameWithConfig { get; set; } = string.Empty;

        public int LapCount { get; set; }

        public double BestLapSeconds { get; set; }

        public DateTime LastRecordedUtc { get; set; }
    }

    public class RecordedLapView
    {
        public string GameName { get; set; } = string.Empty;

        public string CarModel { get; set; } = string.Empty;

        public string TrackName { get; set; } = string.Empty;

        public string TrackNameWithConfig { get; set; } = string.Empty;

        public int LapNumber { get; set; }

        public double LapTimeSeconds { get; set; }

        public double Sector1Seconds { get; set; }

        public double Sector2Seconds { get; set; }

        public double Sector3Seconds { get; set; }

        public bool IsValid { get; set; }

        public DateTime TimestampUtc { get; set; }
    }

    public class GameHistoryTab
    {
        public string GameName { get; set; } = string.Empty;

        public List<StoredTrackSummary> Tracks { get; set; } = new List<StoredTrackSummary>();

        public override string ToString()
        {
            return GameName;
        }
    }
}
