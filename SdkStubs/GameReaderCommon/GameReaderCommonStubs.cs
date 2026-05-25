using System;

namespace GameReaderCommon
{
    public class GameData
    {
        public bool GameRunning { get; set; }

        public Guid SessionId { get; set; }

        public string GameName { get; set; } = string.Empty;

        public StatusDataBase NewData { get; set; }

        public StatusDataBase OldData { get; set; }
    }

    public abstract class StatusDataBase
    {
        public string CarModel { get; set; } = string.Empty;

        public int CompletedLaps { get; set; }

        public bool IsLapValid { get; set; }

        public TimeSpan? LastLapTime { get; set; }

        public TimeSpan? Sector1Time { get; set; }

        public TimeSpan? Sector2Time { get; set; }

        public double SpeedKmh { get; set; }

        public string TrackName { get; set; } = string.Empty;

        public string TrackNameWithConfig { get; set; } = string.Empty;

        public abstract object GetRawDataObject();
    }
}
