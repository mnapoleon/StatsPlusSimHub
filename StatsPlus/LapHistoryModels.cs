using System;
using System.Collections.Generic;

namespace StatsPlus
{
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

        public string CarModelDisplay { get; set; } = string.Empty;

        public string TrackName { get; set; } = string.Empty;

        public string TrackNameWithConfig { get; set; } = string.Empty;

        public string TrackNameWithConfigDisplay { get; set; } = string.Empty;

        public string CircuitNameDisplay { get; set; } = string.Empty;

        public string CircuitLayoutDisplay { get; set; } = string.Empty;

        public int LapCount { get; set; }

        public double BestLapSeconds { get; set; }

        public DateTime LastRecordedUtc { get; set; }
    }

    public class RecordedLapView
    {
        public long LapId { get; set; }

        public string GameName { get; set; } = string.Empty;

        public string CarModel { get; set; } = string.Empty;

        public string CarModelDisplay { get; set; } = string.Empty;

        public string TrackName { get; set; } = string.Empty;

        public string TrackNameWithConfig { get; set; } = string.Empty;

        public string TrackNameWithConfigDisplay { get; set; } = string.Empty;

        public string CircuitNameDisplay { get; set; } = string.Empty;

        public string CircuitLayoutDisplay { get; set; } = string.Empty;

        public int LapNumber { get; set; }

        public double LapTimeSeconds { get; set; }

        public double Sector1Seconds { get; set; }

        public double Sector2Seconds { get; set; }

        public double Sector3Seconds { get; set; }

        public bool IsValid { get; set; }

        public DateTime TimestampUtc { get; set; }
    }

    public class StatsPlusSettingsTab
    {
        public string Header => "Settings";
    }

    public class StatsPlusHistoryTab
    {
        public string Header => "History";
    }
}
