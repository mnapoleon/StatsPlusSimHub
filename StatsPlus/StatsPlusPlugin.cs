using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameReaderCommon;
using Newtonsoft.Json;
using SimHub.Plugins;

namespace StatsPlus
{
    [PluginName("StatsPlus")]
    [PluginDescription("Records lap and sector times by game, car, and track.")]
    [PluginAuthor("StatsPlus")]
    public class StatsPlusPlugin : IPlugin, IDataPlugin, IWPFSettingsV2, INotifyPropertyChanged
    {
        private const string SettingsFileName = "StatsPlus.settings.json";
        private const string DataFileName = "StatsPlus.laps.json";
        private const string Version = "0.2.0";

        private bool _hasLoggedDataError;
        private string _settingsPath = string.Empty;
        private string _databasePath = string.Empty;
        private string _acTrackMapPath = string.Empty;
        private LapDatabase _database = new LapDatabase();
        private Dictionary<string, string> _assettoCorsaTrackMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private double _sessionBestLapSeconds;
        private double _lastLapSeconds;
        private int _sessionLapCount;
        private double _lastSector1Seconds;
        private double _lastSector2Seconds;
        private double _lastSector3Seconds;
        private double _bestSector1Seconds;
        private double _bestSector2Seconds;
        private string _currentGameName = "No active game";
        private string _currentCarModel = "Unknown car";
        private string _currentTrackName = "Unknown track";
        private string _currentTrackNameWithConfig = "Unknown track variation";
        private string _dataStatus = "Waiting for telemetry";
        private bool _pendingLapCapture;
        private int _pendingCompletedLapCount = -1;
        private double _capturedSector1Seconds;
        private double _capturedSector2Seconds;
        private bool _capturedSector1;
        private bool _capturedSector2;
        private StoredTrackSummary _selectedTrackSummary;
        private RecordedLapView _selectedLap;
        private GameHistoryTab _selectedGameHistoryTab;
        private ImageSource _pictureIcon;
        private readonly HashSet<string> _registeredPersonalBestProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public event PropertyChangedEventHandler PropertyChanged;

        public PluginManager PluginManager { get; set; }

        public PluginSettings Settings { get; private set; } = new PluginSettings();

        public ImageSource PictureIcon => _pictureIcon ?? (_pictureIcon = CreatePictureIcon());

        public string LeftMenuTitle => "StatsPlus";

        public ObservableCollection<GameHistoryTab> GameHistoryTabs { get; } = new ObservableCollection<GameHistoryTab>();

        public ObservableCollection<RecordedLapView> SelectedTrackLaps { get; } = new ObservableCollection<RecordedLapView>();

        public string DatabasePath => _databasePath;

        public string SelectedTrackCaption => SelectedTrackSummary == null
            ? "Select a track row above to inspect recorded laps."
            : $"{SelectedTrackSummary.GameName} / {SelectedTrackSummary.CarModel} / {SelectedTrackSummary.TrackNameWithConfigDisplay}";

        public bool HasSelectedLap => SelectedLap != null;

        public bool HasSelectedGameHistoryTab => SelectedGameHistoryTab != null;

        public string CurrentContext => $"{CurrentGameName} / {CurrentCarModel} / {GetDisplayTrackNameWithConfig(CurrentGameName, CurrentTrackNameWithConfig)}";

        public string CurrentGameName
        {
            get => _currentGameName;
            private set
            {
                if (_currentGameName == value)
                {
                    return;
                }

                _currentGameName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentContext));
            }
        }

        public string CurrentCarModel
        {
            get => _currentCarModel;
            private set
            {
                if (_currentCarModel == value)
                {
                    return;
                }

                _currentCarModel = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentContext));
            }
        }

        public string CurrentTrackName
        {
            get => _currentTrackName;
            private set
            {
                if (_currentTrackName == value)
                {
                    return;
                }

                _currentTrackName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentContext));
            }
        }

        public string CurrentTrackNameWithConfig
        {
            get => _currentTrackNameWithConfig;
            private set
            {
                if (_currentTrackNameWithConfig == value)
                {
                    return;
                }

                _currentTrackNameWithConfig = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentContext));
            }
        }

        public string DataStatus
        {
            get => _dataStatus;
            private set
            {
                if (_dataStatus == value)
                {
                    return;
                }

                _dataStatus = value;
                OnPropertyChanged();
            }
        }

        public double SessionBestLapSeconds
        {
            get => _sessionBestLapSeconds;
            private set
            {
                if (Math.Abs(_sessionBestLapSeconds - value) < 0.0001)
                {
                    return;
                }

                _sessionBestLapSeconds = value;
                OnPropertyChanged();
            }
        }

        public double LastLapSeconds
        {
            get => _lastLapSeconds;
            private set
            {
                if (Math.Abs(_lastLapSeconds - value) < 0.0001)
                {
                    return;
                }

                _lastLapSeconds = value;
                OnPropertyChanged();
            }
        }

        public int SessionLapCount
        {
            get => _sessionLapCount;
            private set
            {
                if (_sessionLapCount == value)
                {
                    return;
                }

                _sessionLapCount = value;
                OnPropertyChanged();
            }
        }

        public double LastSector1Seconds
        {
            get => _lastSector1Seconds;
            private set
            {
                if (Math.Abs(_lastSector1Seconds - value) < 0.0001)
                {
                    return;
                }

                _lastSector1Seconds = value;
                OnPropertyChanged();
            }
        }

        public double LastSector2Seconds
        {
            get => _lastSector2Seconds;
            private set
            {
                if (Math.Abs(_lastSector2Seconds - value) < 0.0001)
                {
                    return;
                }

                _lastSector2Seconds = value;
                OnPropertyChanged();
            }
        }

        public double LastSector3Seconds
        {
            get => _lastSector3Seconds;
            private set
            {
                if (Math.Abs(_lastSector3Seconds - value) < 0.0001)
                {
                    return;
                }

                _lastSector3Seconds = value;
                OnPropertyChanged();
            }
        }

        public double AllTimeBestLapSeconds { get; private set; }

        public StoredTrackSummary SelectedTrackSummary
        {
            get => _selectedTrackSummary;
            set
            {
                if (ReferenceEquals(_selectedTrackSummary, value))
                {
                    return;
                }

                _selectedTrackSummary = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedTrackCaption));
                LoadSelectedTrackLaps();
            }
        }

        public RecordedLapView SelectedLap
        {
            get => _selectedLap;
            set
            {
                if (ReferenceEquals(_selectedLap, value))
                {
                    return;
                }

                _selectedLap = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedLap));
            }
        }

        public GameHistoryTab SelectedGameHistoryTab
        {
            get => _selectedGameHistoryTab;
            set
            {
                if (ReferenceEquals(_selectedGameHistoryTab, value))
                {
                    return;
                }

                _selectedGameHistoryTab = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedGameHistoryTab));
            }
        }

        public void Init(PluginManager pluginManager)
        {
            PluginManager = pluginManager;
            _settingsPath = pluginManager.GetCommonStoragePath(SettingsFileName);
            _databasePath = pluginManager.GetCommonStoragePath(DataFileName);
            _acTrackMapPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ac_track_id_map.json");
            Settings = LoadSettings();
            _assettoCorsaTrackMap = LoadAssettoCorsaTrackMap();
            _database = LoadDatabase();

            pluginManager.AddProperty("StatsPlus.Version", GetType(), Version);
            pluginManager.AddProperty("StatsPlus.Enabled", GetType(), Settings.EnablePlugin);
            pluginManager.AddProperty("StatsPlus.Label", GetType(), Settings.CustomLabel);
            pluginManager.AddProperty("StatsPlus.GameName", GetType(), string.Empty);
            pluginManager.AddProperty("StatsPlus.TrackName", GetType(), string.Empty);
            pluginManager.AddProperty("StatsPlus.CarModel", GetType(), string.Empty);
            pluginManager.AddProperty("StatsPlus.SpeedKmh", GetType(), 0.0);
            pluginManager.AddProperty("StatsPlus.IsGameRunning", GetType(), false);
            pluginManager.AddProperty("StatsPlus.LastLapTime", GetType(), 0.0);
            pluginManager.AddProperty("StatsPlus.SessionBestLapTime", GetType(), 0.0);
            pluginManager.AddProperty("StatsPlus.AllTimeBestLapTime", GetType(), 0.0);
            pluginManager.AddProperty("StatsPlus.SessionLapCount", GetType(), 0);
            pluginManager.AddProperty("StatsPlus.LastSector1Time", GetType(), 0.0);
            pluginManager.AddProperty("StatsPlus.LastSector2Time", GetType(), 0.0);
            pluginManager.AddProperty("StatsPlus.LastSector3Time", GetType(), 0.0);
            pluginManager.AddProperty("StatsPlus.DataFilePath", GetType(), _databasePath);

            RefreshPersonalBestProperties(pluginManager);
            RefreshStoredTrackSummaries();
            SimHub.Logging.Current.Info($"StatsPlus v{Version} - Initialised");
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            try
            {
                pluginManager.SetPropertyValue("StatsPlus.Enabled", GetType(), Settings.EnablePlugin);
                pluginManager.SetPropertyValue("StatsPlus.Label", GetType(), Settings.CustomLabel);
                pluginManager.SetPropertyValue("StatsPlus.IsGameRunning", GetType(), data.GameRunning);
                pluginManager.SetPropertyValue("StatsPlus.DataFilePath", GetType(), _databasePath);

                if (!Settings.EnablePlugin || !data.GameRunning || data.NewData == null)
                {
                    DataStatus = !Settings.EnablePlugin ? "Plugin disabled" : "Waiting for telemetry";
                    ClearLiveTelemetryProperties(pluginManager);
                    _hasLoggedDataError = false;
                    return;
                }

                string gameName = NormalizeContextValue(data.GameName, "Unknown Game");
                if (!IsGameRecordingEnabled(gameName))
                {
                    DataStatus = $"Recording disabled for {gameName}";
                    ClearLiveTelemetryProperties(pluginManager);
                    _hasLoggedDataError = false;
                    return;
                }

                string carModel = NormalizeContextValue(data.NewData.CarModel, "Unknown Car");
                string trackName = NormalizeContextValue(data.NewData.TrackName, "Unknown Track");
                string trackNameWithConfig = NormalizeContextValue(data.NewData.TrackNameWithConfig, trackName);

                if (!IsSameContext(gameName, carModel, trackName, trackNameWithConfig))
                {
                    SwitchContext(gameName, carModel, trackName, trackNameWithConfig, pluginManager);
                }

                pluginManager.SetPropertyValue("StatsPlus.SpeedKmh", GetType(), data.NewData.SpeedKmh);

                if (Settings.PublishTrackInfo)
                {
                    pluginManager.SetPropertyValue("StatsPlus.GameName", GetType(), gameName);
                    pluginManager.SetPropertyValue("StatsPlus.TrackName", GetType(), GetDisplayTrackNameWithConfig(gameName, trackNameWithConfig));
                    pluginManager.SetPropertyValue("StatsPlus.CarModel", GetType(), carModel);
                }

                CaptureSectorData(data);
                FinalizePendingLapIfReady(pluginManager, data, gameName, carModel, trackName, trackNameWithConfig);
                QueueLapCaptureIfNeeded(data);
                PublishLapProperties(pluginManager);

                DataStatus = "Recording telemetry";
                _hasLoggedDataError = false;
            }
            catch (Exception ex)
            {
                if (_hasLoggedDataError)
                {
                    return;
                }

                SimHub.Logging.Current.Error($"StatsPlus - DataUpdate error: {ex}");
                _hasLoggedDataError = true;
            }
        }

        public void End(PluginManager pluginManager)
        {
            SaveDatabase();
            SaveSettings();
            SimHub.Logging.Current.Info("StatsPlus - Shutting down");
        }

        public Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            return new SettingsControl(this)
            {
                DataContext = this
            };
        }

        public System.Windows.Forms.Control GetSettingsControl(PluginManager pluginManager)
        {
            return null;
        }

        private PluginSettings LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    return new PluginSettings();
                }

                var json = File.ReadAllText(_settingsPath, Encoding.UTF8);
                var settings = JsonConvert.DeserializeObject<PluginSettings>(json);
                return settings ?? new PluginSettings();
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn($"StatsPlus - Failed to load settings, using defaults: {ex.Message}");
                return new PluginSettings();
            }
        }

        internal void ResetSettings()
        {
            Settings.Reset();
            SaveSettings();
        }

        internal void RefreshStoredTrackSummaries()
        {
            List<StoredTrackSummary> summaries = BuildTrackSummaries()
                .OrderBy(summary => summary.GameName)
                .ThenBy(summary => summary.CarModel)
                .ThenBy(summary => summary.TrackName)
                .ToList();

            List<GameHistoryTab> tabs = summaries
                .GroupBy(summary => summary.GameName)
                .Select(group => new GameHistoryTab
                {
                    GameName = group.Key,
                    Tracks = group
                        .OrderBy(summary => summary.CarModel)
                        .ThenBy(summary => summary.TrackName)
                        .ThenBy(summary => summary.TrackNameWithConfig)
                        .ToList()
                })
                .OrderBy(tab => tab.GameName)
                .ToList();

            void Apply()
            {
                string selectedGame = SelectedTrackSummary?.GameName;
                string selectedCar = SelectedTrackSummary?.CarModel;
                string selectedTrackConfig = SelectedTrackSummary?.TrackNameWithConfig;

                GameHistoryTabs.Clear();
                foreach (GameHistoryTab tab in tabs)
                {
                    GameHistoryTabs.Add(tab);
                }

                SelectedTrackSummary = summaries.FirstOrDefault(summary =>
                    string.Equals(summary.GameName, selectedGame, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(summary.CarModel, selectedCar, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(summary.TrackNameWithConfig, selectedTrackConfig, StringComparison.OrdinalIgnoreCase));
            }

            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.BeginInvoke((Action)Apply);
            }
            else
            {
                Apply();
            }
        }

        internal void SaveSettings()
        {
            try
            {
                var directory = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonConvert.SerializeObject(Settings, Formatting.Indented);
                File.WriteAllText(_settingsPath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error($"StatsPlus - Failed to save settings: {ex.Message}");
            }
        }

        internal void ToggleSelectedLapValidity()
        {
            if (SelectedLap == null)
            {
                return;
            }

            if (!TryGetTrackBucket(SelectedLap.GameName, SelectedLap.CarModel, SelectedLap.TrackNameWithConfig, out TrackBucket trackBucket))
            {
                return;
            }

            RecordedLap lap = trackBucket.Laps.FirstOrDefault(candidate =>
                candidate.LapNumber == SelectedLap.LapNumber &&
                candidate.TimestampUtc == SelectedLap.TimestampUtc);

            if (lap == null)
            {
                return;
            }

            lap.IsValid = !lap.IsValid;
            trackBucket.LastUpdatedUtc = DateTime.UtcNow;
            SaveDatabase();
            LoadSelectedTrackLaps(SelectedLap.TimestampUtc);
            RefreshStoredTrackSummaries();
            RefreshPersonalBestProperties(PluginManager);

            if (IsSameContext(SelectedLap.GameName, SelectedLap.CarModel, SelectedLap.TrackName, SelectedLap.TrackNameWithConfig))
            {
                AllTimeBestLapSeconds = GetBestLapSeconds(SelectedLap.GameName, SelectedLap.CarModel, SelectedLap.TrackNameWithConfig);
                OnPropertyChanged(nameof(AllTimeBestLapSeconds));
            }
        }

        internal void ClearSelectedGameData()
        {
            if (SelectedGameHistoryTab == null)
            {
                return;
            }

            _database.Games.Remove(SelectedGameHistoryTab.GameName);
            SaveDatabase();

            if (string.Equals(CurrentGameName, SelectedGameHistoryTab.GameName, StringComparison.OrdinalIgnoreCase))
            {
                SessionLapCount = 0;
                SessionBestLapSeconds = 0.0;
                LastLapSeconds = 0.0;
                LastSector1Seconds = 0.0;
                LastSector2Seconds = 0.0;
                LastSector3Seconds = 0.0;
                AllTimeBestLapSeconds = 0.0;
                OnPropertyChanged(nameof(AllTimeBestLapSeconds));
                DataStatus = $"Cleared stored data for {SelectedGameHistoryTab.GameName}";
            }

            SelectedLap = null;
            SelectedTrackSummary = null;
            RefreshPersonalBestProperties(PluginManager);
            RefreshStoredTrackSummaries();
        }

        internal void ClearAllData()
        {
            _database = new LapDatabase();
            SaveDatabase();

            SessionLapCount = 0;
            SessionBestLapSeconds = 0.0;
            LastLapSeconds = 0.0;
            LastSector1Seconds = 0.0;
            LastSector2Seconds = 0.0;
            LastSector3Seconds = 0.0;
            AllTimeBestLapSeconds = 0.0;
            OnPropertyChanged(nameof(AllTimeBestLapSeconds));
            DataStatus = "Cleared all stored lap data";

            SelectedLap = null;
            SelectedTrackSummary = null;
            SelectedGameHistoryTab = null;
            RefreshPersonalBestProperties(PluginManager);
            RefreshStoredTrackSummaries();
        }

        private LapDatabase LoadDatabase()
        {
            try
            {
                if (!File.Exists(_databasePath))
                {
                    return new LapDatabase();
                }

                string json = File.ReadAllText(_databasePath, Encoding.UTF8);
                LapDatabase database = JsonConvert.DeserializeObject<LapDatabase>(json);
                database = database ?? new LapDatabase();
                NormalizeLoadedDatabase(database);
                return database;
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn($"StatsPlus - Failed to load lap database, using empty data: {ex.Message}");
                return new LapDatabase();
            }
        }

        private Dictionary<string, string> LoadAssettoCorsaTrackMap()
        {
            try
            {
                if (!File.Exists(_acTrackMapPath))
                {
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                string json = File.ReadAllText(_acTrackMapPath, Encoding.UTF8);
                Dictionary<string, string> map = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                return map ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn($"StatsPlus - Failed to load AC track map: {ex.Message}");
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void SaveDatabase()
        {
            try
            {
                string directory = Path.GetDirectoryName(_databasePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonConvert.SerializeObject(_database, Formatting.Indented);
                File.WriteAllText(_databasePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error($"StatsPlus - Failed to save lap database: {ex.Message}");
            }
        }

        private void SwitchContext(string gameName, string carModel, string trackName, string trackNameWithConfig, PluginManager pluginManager)
        {
            CurrentGameName = gameName;
            CurrentCarModel = carModel;
            CurrentTrackName = trackName;
            CurrentTrackNameWithConfig = trackNameWithConfig;
            SessionLapCount = 0;
            SessionBestLapSeconds = 0.0;
            LastLapSeconds = 0.0;
            LastSector1Seconds = 0.0;
            LastSector2Seconds = 0.0;
            LastSector3Seconds = 0.0;
            _capturedSector1Seconds = 0.0;
            _capturedSector2Seconds = 0.0;
            _capturedSector1 = false;
            _capturedSector2 = false;
            _pendingLapCapture = false;
            _pendingCompletedLapCount = -1;
            _bestSector1Seconds = 0.0;
            _bestSector2Seconds = 0.0;
            AllTimeBestLapSeconds = GetBestLapSeconds(gameName, carModel, trackNameWithConfig);
            DataStatus = $"Recording {CurrentContext}";
            OnPropertyChanged(nameof(AllTimeBestLapSeconds));
            PublishLapProperties(pluginManager);
        }

        private bool IsSameContext(string gameName, string carModel, string trackName, string trackNameWithConfig)
        {
            return string.Equals(CurrentGameName, gameName, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(CurrentCarModel, carModel, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(CurrentTrackName, trackName, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(CurrentTrackNameWithConfig, trackNameWithConfig, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeContextValue(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private void CaptureSectorData(GameData data)
        {
            if (data.NewData == null)
            {
                return;
            }

            double sector1 = ToSeconds(data.NewData.Sector1Time);
            double sector2 = ToSeconds(data.NewData.Sector2Time);

            if (sector1 > 0)
            {
                _capturedSector1Seconds = sector1;
                _capturedSector1 = true;
            }

            if (sector2 > 0)
            {
                _capturedSector2Seconds = sector2;
                _capturedSector2 = true;
            }
        }

        private void QueueLapCaptureIfNeeded(GameData data)
        {
            if (data.NewData == null || data.OldData == null)
            {
                return;
            }

            if (data.NewData.CompletedLaps != data.OldData.CompletedLaps && data.NewData.CompletedLaps >= 1)
            {
                _pendingLapCapture = true;
                _pendingCompletedLapCount = data.NewData.CompletedLaps;
            }
        }

        private void FinalizePendingLapIfReady(PluginManager pluginManager, GameData data, string gameName, string carModel, string trackName, string trackNameWithConfig)
        {
            if (!_pendingLapCapture || data.NewData == null)
            {
                return;
            }

            double lapTime = ToSeconds(data.NewData.LastLapTime);
            if (lapTime <= 0)
            {
                return;
            }

            double sector1 = _capturedSector1 ? _capturedSector1Seconds : 0.0;
            double sector2 = _capturedSector2 ? _capturedSector2Seconds : 0.0;
            double sector3 = 0.0;

            InferSectorLayout(gameName, lapTime, ref sector1, ref sector2, ref sector3);

            bool isValid = data.NewData.IsLapValid;
            RecordedLap lap = new RecordedLap
            {
                LapNumber = _pendingCompletedLapCount,
                LapTimeSeconds = lapTime,
                Sector1Seconds = sector1,
                Sector2Seconds = sector2,
                Sector3Seconds = sector3,
                IsValid = isValid,
                TimestampUtc = DateTime.UtcNow
            };

            AddLapToDatabase(gameName, carModel, trackName, trackNameWithConfig, lap);

            SessionLapCount += 1;
            LastLapSeconds = lap.LapTimeSeconds;
            LastSector1Seconds = lap.Sector1Seconds;
            LastSector2Seconds = lap.Sector2Seconds;
            LastSector3Seconds = lap.Sector3Seconds;
            SessionBestLapSeconds = lap.IsValid ? UpdateBest(SessionBestLapSeconds, lap.LapTimeSeconds) : SessionBestLapSeconds;
            AllTimeBestLapSeconds = lap.IsValid ? UpdateBest(AllTimeBestLapSeconds, lap.LapTimeSeconds) : AllTimeBestLapSeconds;
            _bestSector1Seconds = UpdateBest(_bestSector1Seconds, lap.Sector1Seconds);
            _bestSector2Seconds = UpdateBest(_bestSector2Seconds, lap.Sector2Seconds);

            OnPropertyChanged(nameof(AllTimeBestLapSeconds));
            DataStatus = $"Saved lap {lap.LapNumber} to {CurrentContext}";

            _pendingLapCapture = false;
            _pendingCompletedLapCount = -1;
            _capturedSector1Seconds = 0.0;
            _capturedSector2Seconds = 0.0;
            _capturedSector1 = false;
            _capturedSector2 = false;

            PublishLapProperties(pluginManager);
            RefreshStoredTrackSummaries();
        }

        private void AddLapToDatabase(string gameName, string carModel, string trackName, string trackNameWithConfig, RecordedLap lap)
        {
            TrackBucket track = GetOrCreateTrackBucket(gameName, carModel, trackName, trackNameWithConfig);
            track.LastUpdatedUtc = DateTime.UtcNow;
            track.Laps.Add(lap);
            SaveDatabase();
            RefreshPersonalBestProperties(PluginManager);
        }

        private TrackBucket GetOrCreateTrackBucket(string gameName, string carModel, string trackName, string trackNameWithConfig)
        {
            if (!_database.Games.TryGetValue(gameName, out GameBucket gameBucket))
            {
                gameBucket = new GameBucket();
                _database.Games[gameName] = gameBucket;
            }

            if (!gameBucket.Cars.TryGetValue(carModel, out CarBucket carBucket))
            {
                carBucket = new CarBucket();
                gameBucket.Cars[carModel] = carBucket;
            }

            if (!carBucket.Tracks.TryGetValue(trackNameWithConfig, out TrackBucket trackBucket))
            {
                trackBucket = new TrackBucket
                {
                    GameName = gameName,
                    CarModel = carModel,
                    TrackName = trackName,
                    TrackNameWithConfig = trackNameWithConfig,
                    CreatedUtc = DateTime.UtcNow,
                    LastUpdatedUtc = DateTime.UtcNow
                };
                carBucket.Tracks[trackNameWithConfig] = trackBucket;
            }

            return trackBucket;
        }

        private double GetBestLapSeconds(string gameName, string carModel, string trackNameWithConfig)
        {
            if (!TryGetTrackBucket(gameName, carModel, trackNameWithConfig, out TrackBucket trackBucket))
            {
                return 0.0;
            }

            return trackBucket.Laps
                .Where(lap => lap.IsValid && lap.LapTimeSeconds > 0)
                .Select(lap => lap.LapTimeSeconds)
                .DefaultIfEmpty(0.0)
                .Min();
        }

        private IEnumerable<StoredTrackSummary> BuildTrackSummaries()
        {
            foreach (KeyValuePair<string, GameBucket> gameEntry in _database.Games)
            {
                foreach (KeyValuePair<string, CarBucket> carEntry in gameEntry.Value.Cars)
                {
                    foreach (KeyValuePair<string, TrackBucket> trackEntry in carEntry.Value.Tracks)
                    {
                        TrackBucket track = trackEntry.Value;
                        double bestLap = track.Laps
                            .Where(lap => lap.IsValid && lap.LapTimeSeconds > 0)
                            .Select(lap => lap.LapTimeSeconds)
                            .DefaultIfEmpty(0.0)
                            .Min();

                        yield return new StoredTrackSummary
                        {
                            GameName = gameEntry.Key,
                            CarModel = carEntry.Key,
                            TrackName = track.TrackName,
                            TrackNameWithConfig = string.IsNullOrWhiteSpace(track.TrackNameWithConfig) ? trackEntry.Key : track.TrackNameWithConfig,
                            TrackNameWithConfigDisplay = GetDisplayTrackNameWithConfig(gameEntry.Key, string.IsNullOrWhiteSpace(track.TrackNameWithConfig) ? trackEntry.Key : track.TrackNameWithConfig),
                            LapCount = track.Laps.Count,
                            BestLapSeconds = bestLap,
                            LastRecordedUtc = track.LastUpdatedUtc
                        };
                    }
                }
            }
        }

        private void RefreshPersonalBestProperties(PluginManager pluginManager)
        {
            if (pluginManager == null)
            {
                return;
            }

            Dictionary<string, double> personalBestValues = BuildPersonalBestPropertyValues();

            foreach (string propertyName in _registeredPersonalBestProperties.Except(personalBestValues.Keys).ToList())
            {
                pluginManager.SetPropertyValue(propertyName, GetType(), 0.0);
            }

            foreach (KeyValuePair<string, double> property in personalBestValues)
            {
                EnsurePersonalBestPropertyRegistered(pluginManager, property.Key);
                pluginManager.SetPropertyValue(property.Key, GetType(), property.Value);
            }
        }

        private Dictionary<string, double> BuildPersonalBestPropertyValues()
        {
            var personalBestValues = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, GameBucket> gameEntry in _database.Games)
            {
                foreach (KeyValuePair<string, CarBucket> carEntry in gameEntry.Value.Cars)
                {
                    foreach (KeyValuePair<string, TrackBucket> trackEntry in carEntry.Value.Tracks)
                    {
                        TrackBucket track = trackEntry.Value;
                        string trackVariation = string.IsNullOrWhiteSpace(track.TrackNameWithConfig) ? trackEntry.Key : track.TrackNameWithConfig;
                        double bestLap = track.Laps
                            .Where(lap => lap.IsValid && lap.LapTimeSeconds > 0)
                            .Select(lap => lap.LapTimeSeconds)
                            .DefaultIfEmpty(0.0)
                            .Min();

                        if (bestLap <= 0)
                        {
                            continue;
                        }

                        personalBestValues[BuildPersonalBestPropertyName(gameEntry.Key, carEntry.Key, trackVariation)] = bestLap;
                    }
                }
            }

            return personalBestValues;
        }

        private void EnsurePersonalBestPropertyRegistered(PluginManager pluginManager, string propertyName)
        {
            if (_registeredPersonalBestProperties.Contains(propertyName))
            {
                return;
            }

            pluginManager.AddProperty(propertyName, GetType(), 0.0);
            _registeredPersonalBestProperties.Add(propertyName);
        }

        private static string BuildPersonalBestPropertyName(string gameName, string carModel, string trackVariation)
        {
            return $"StatsPlus.PersonalBest.{SanitizePropertySegment(gameName)}.{SanitizePropertySegment(carModel)}.{SanitizePropertySegment(trackVariation)}";
        }

        private static string SanitizePropertySegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Unknown";
            }

            string sanitized = Regex.Replace(value.Trim(), @"[^A-Za-z0-9]+", "_").Trim('_');
            return string.IsNullOrWhiteSpace(sanitized) ? "Unknown" : sanitized;
        }

        private void PublishLapProperties(PluginManager pluginManager)
        {
            pluginManager.SetPropertyValue("StatsPlus.LastLapTime", GetType(), LastLapSeconds);
            pluginManager.SetPropertyValue("StatsPlus.SessionBestLapTime", GetType(), SessionBestLapSeconds);
            pluginManager.SetPropertyValue("StatsPlus.AllTimeBestLapTime", GetType(), AllTimeBestLapSeconds);
            pluginManager.SetPropertyValue("StatsPlus.SessionLapCount", GetType(), SessionLapCount);
            pluginManager.SetPropertyValue("StatsPlus.LastSector1Time", GetType(), LastSector1Seconds);
            pluginManager.SetPropertyValue("StatsPlus.LastSector2Time", GetType(), LastSector2Seconds);
            pluginManager.SetPropertyValue("StatsPlus.LastSector3Time", GetType(), LastSector3Seconds);
        }

        private static double UpdateBest(double currentBest, double candidate)
        {
            if (candidate <= 0)
            {
                return currentBest;
            }

            return currentBest <= 0 || candidate < currentBest ? candidate : currentBest;
        }

        private static double ToSeconds(TimeSpan? value)
        {
            return value.HasValue ? value.Value.TotalSeconds : 0.0;
        }

        private void NormalizeLoadedDatabase(LapDatabase database)
        {
            foreach (KeyValuePair<string, GameBucket> gameEntry in database.Games)
            {
                foreach (KeyValuePair<string, CarBucket> carEntry in gameEntry.Value.Cars)
                {
                    foreach (KeyValuePair<string, TrackBucket> trackEntry in carEntry.Value.Tracks)
                    {
                        foreach (RecordedLap lap in trackEntry.Value.Laps)
                        {
                            double sector1 = lap.Sector1Seconds;
                            double sector2 = lap.Sector2Seconds;
                            double sector3 = lap.Sector3Seconds;

                            InferSectorLayout(gameEntry.Key, lap.LapTimeSeconds, ref sector1, ref sector2, ref sector3);

                            lap.Sector1Seconds = sector1;
                            lap.Sector2Seconds = sector2;
                            lap.Sector3Seconds = sector3;
                        }
                    }
                }
            }
        }

        private void InferSectorLayout(string gameName, double lapTime, ref double sector1, ref double sector2, ref double sector3)
        {
            if (lapTime <= 0)
            {
                sector1 = 0.0;
                sector2 = 0.0;
                sector3 = 0.0;
                return;
            }

            if (sector1 > 0 && sector2 > 0)
            {
                sector3 = Math.Max(0.0, lapTime - sector1 - sector2);
                return;
            }

            if (IsAssettoCorsaGame(gameName) && sector1 > 0 && sector2 <= 0)
            {
                sector2 = Math.Max(0.0, lapTime - sector1);
                sector3 = 0.0;
                return;
            }

            sector3 = lapTime;
        }

        private bool IsAssettoCorsaGame(string gameName)
        {
            return string.Equals(NormalizeGameName(gameName), "assettocorsa", StringComparison.Ordinal);
        }

        private void ClearLiveTelemetryProperties(PluginManager pluginManager)
        {
            pluginManager.SetPropertyValue("StatsPlus.SpeedKmh", GetType(), 0.0);
            pluginManager.SetPropertyValue("StatsPlus.LastLapTime", GetType(), 0.0);
            pluginManager.SetPropertyValue("StatsPlus.SessionBestLapTime", GetType(), 0.0);
            pluginManager.SetPropertyValue("StatsPlus.AllTimeBestLapTime", GetType(), 0.0);
            pluginManager.SetPropertyValue("StatsPlus.SessionLapCount", GetType(), 0);
            pluginManager.SetPropertyValue("StatsPlus.LastSector1Time", GetType(), 0.0);
            pluginManager.SetPropertyValue("StatsPlus.LastSector2Time", GetType(), 0.0);
            pluginManager.SetPropertyValue("StatsPlus.LastSector3Time", GetType(), 0.0);

            if (Settings.PublishTrackInfo)
            {
                pluginManager.SetPropertyValue("StatsPlus.GameName", GetType(), string.Empty);
                pluginManager.SetPropertyValue("StatsPlus.TrackName", GetType(), string.Empty);
                pluginManager.SetPropertyValue("StatsPlus.CarModel", GetType(), string.Empty);
            }
        }

        private bool IsGameRecordingEnabled(string gameName)
        {
            string normalized = NormalizeGameName(gameName);

            switch (normalized)
            {
                case "assettocorsa":
                    return Settings.RecordAssettoCorsa;
                case "assettocorsaevo":
                    return Settings.RecordAssettoCorsaEvo;
                case "automobilista2":
                    return Settings.RecordAutomobilista2;
                case "iracing":
                    return Settings.RecordIRacing;
                case "rfactor2":
                    return Settings.RecordRFactor2;
                case "raceroomracingexperience":
                case "r3e":
                case "rrre":
                    return Settings.RecordR3E;
                default:
                    return false;
            }
        }

        private static string NormalizeGameName(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(gameName.Length);
            foreach (char character in gameName)
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                }
            }

            return builder.ToString();
        }

        private bool TryGetTrackBucket(string gameName, string carModel, string trackNameWithConfig, out TrackBucket trackBucket)
        {
            trackBucket = null;

            if (!_database.Games.TryGetValue(gameName, out GameBucket gameBucket))
            {
                return false;
            }

            if (!gameBucket.Cars.TryGetValue(carModel, out CarBucket carBucket))
            {
                return false;
            }

            return carBucket.Tracks.TryGetValue(trackNameWithConfig, out trackBucket);
        }

        private void LoadSelectedTrackLaps(DateTime? selectedTimestamp = null)
        {
            List<RecordedLapView> lapViews = new List<RecordedLapView>();

            if (SelectedTrackSummary != null &&
                TryGetTrackBucket(SelectedTrackSummary.GameName, SelectedTrackSummary.CarModel, SelectedTrackSummary.TrackNameWithConfig, out TrackBucket trackBucket))
            {
                lapViews = trackBucket.Laps
                    .OrderByDescending(lap => lap.TimestampUtc)
                    .Select(lap => new RecordedLapView
                    {
                        GameName = trackBucket.GameName,
                        CarModel = trackBucket.CarModel,
                        TrackName = trackBucket.TrackName,
                        TrackNameWithConfig = trackBucket.TrackNameWithConfig,
                        TrackNameWithConfigDisplay = GetDisplayTrackNameWithConfig(trackBucket.GameName, trackBucket.TrackNameWithConfig),
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

            SelectedTrackLaps.Clear();
            foreach (RecordedLapView lapView in lapViews)
            {
                SelectedTrackLaps.Add(lapView);
            }

            SelectedLap = selectedTimestamp.HasValue
                ? SelectedTrackLaps.FirstOrDefault(lap => lap.TimestampUtc == selectedTimestamp.Value)
                : null;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static ImageSource CreatePictureIcon()
        {
            BitmapImage image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri("pack://application:,,,/StatsPlus;component/assets/statsplus-icon-24.png", UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }

        private string GetDisplayTrackNameWithConfig(string gameName, string rawTrackNameWithConfig)
        {
            if (!IsAssettoCorsaGame(gameName))
            {
                return rawTrackNameWithConfig;
            }

            if (string.IsNullOrWhiteSpace(rawTrackNameWithConfig))
            {
                return rawTrackNameWithConfig;
            }

            return _assettoCorsaTrackMap.TryGetValue(rawTrackNameWithConfig, out string mappedName)
                ? mappedName
                : rawTrackNameWithConfig;
        }
    }
}
