using System;
using System.Collections.Generic;

namespace Affinity
{
    public class AffinityDatabase
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

        public double TotalDistanceMeters { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    }

    public class DistanceSummary
    {
        public string GameName { get; set; } = string.Empty;

        public string CarModel { get; set; } = string.Empty;

        public string TrackName { get; set; } = string.Empty;

        public string TrackNameWithConfig { get; set; } = string.Empty;

        public double TotalDistanceKm { get; set; }

        public double TotalDistanceMiles { get; set; }

        public DateTime LastUpdatedUtc { get; set; }
    }

    public class TrackDistanceSummary
    {
        public string TrackName { get; set; } = string.Empty;

        public string TrackDisplayName { get; set; } = string.Empty;

        public double DistanceKm { get; set; }

        public double DistanceMiles { get; set; }

        public double DistanceDisplay { get; set; }
    }

    public class CarDistanceSummary
    {
        public string CarModel { get; set; } = string.Empty;

        public double DistanceKm { get; set; }

        public double DistanceMiles { get; set; }

        public double DistanceDisplay { get; set; }
    }

    public class GameDistanceTab
    {
        public string GameName { get; set; } = string.Empty;

        public double TotalDistanceKm { get; set; }

        public double TotalDistanceMiles { get; set; }

        public double TotalDistanceDisplay { get; set; }

        public List<TrackDistanceSummary> TrackSummaries { get; set; } = new List<TrackDistanceSummary>();

        public List<CarDistanceSummary> CarSummaries { get; set; } = new List<CarDistanceSummary>();

        public override string ToString()
        {
            return GameName;
        }
    }
}
