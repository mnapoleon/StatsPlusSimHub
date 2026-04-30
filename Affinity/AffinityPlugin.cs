using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Controls;
using System.Windows.Media;
using GameReaderCommon;
using Newtonsoft.Json;
using SimHub.Plugins;

namespace Affinity
{
    [PluginName("Affinity")]
    [PluginDescription("Tracks cumulative distance by game, car, and track across sessions.")]
    [PluginAuthor("Affinity")]
    public class AffinityPlugin : IPlugin, IDataPlugin, IWPFSettingsV2, INotifyPropertyChanged
    {
        private const string SettingsFileName = "Affinity.settings.json";
        private const string DataFileName = "Affinity.distance.json";
        private const string Version = "0.1.0";
        private const double MetersPerKilometer = 1000.0;
        private const double MetersPerMile = 1609.344;
        private const double SaveThresholdMeters = 50.0;

        private bool _hasLoggedDataError;
        private string _settingsPath = string.Empty;
        private string _databasePath = string.Empty;
        private string _acTrackMapPath = string.Empty;
        private AffinityDatabase _database = new AffinityDatabase();
        private Dictionary<string, string> _assettoCorsaTrackMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string _currentGameName = "No active game";
        private string _currentCarModel = "Unknown car";
        private string _currentTrackName = "Unknown track";
        private string _currentTrackNameWithConfig = "Unknown track variation";
        private string _dataStatus = "Waiting for telemetry";
        private double _currentContextDistanceKm;
        private double _sessionDistanceKm;
        private GameDistanceTab _selectedGameTab;
        private Guid _activeSessionId = Guid.Empty;
        private string _activeContextKey = string.Empty;
        private double _lastObservedSessionMeters = -1.0;
        private double _pendingMetersSinceSave;

        public event PropertyChangedEventHandler PropertyChanged;

        public PluginManager PluginManager { get; set; }

        public AffinitySettings Settings { get; private set; } = new AffinitySettings();

        public ImageSource PictureIcon => null;

        public string LeftMenuTitle => "Affinity";

        public string DatabasePath => _databasePath;

        public ObservableCollection<GameDistanceTab> GameTabs { get; } = new ObservableCollection<GameDistanceTab>();

        public string CurrentContext => $"{CurrentGameName} / {CurrentCarModel} / {GetDisplayTrackNameWithConfig(CurrentGameName, CurrentTrackNameWithConfig)}";

        public string DistanceUnitLabel => Settings.DisplayInMiles ? "Miles" : "KM";

        public string DistanceColumnHeader => Settings.DisplayInMiles ? "Distance (mi)" : "Distance (km)";

        public double CurrentContextDistanceDisplay => Settings.DisplayInMiles
            ? CurrentContextDistanceKm * MetersPerKilometer / MetersPerMile
            : CurrentContextDistanceKm;

        public double SessionDistanceDisplay => Settings.DisplayInMiles
            ? SessionDistanceKm * MetersPerKilometer / MetersPerMile
            : SessionDistanceKm;

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

        public double CurrentContextDistanceKm
        {
            get => _currentContextDistanceKm;
            private set
            {
                if (Math.Abs(_currentContextDistanceKm - value) < 0.0001)
                {
                    return;
                }

                _currentContextDistanceKm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentContextDistanceDisplay));
            }
        }

        public double SessionDistanceKm
        {
            get => _sessionDistanceKm;
            private set
            {
                if (Math.Abs(_sessionDistanceKm - value) < 0.0001)
                {
                    return;
                }

                _sessionDistanceKm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SessionDistanceDisplay));
            }
        }

        public GameDistanceTab SelectedGameTab
        {
            get => _selectedGameTab;
            set
            {
                if (ReferenceEquals(_selectedGameTab, value))
                {
                    return;
                }

                _selectedGameTab = value;
                OnPropertyChanged();
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

            pluginManager.AddProperty("Affinity.Version", GetType(), Version);
            pluginManager.AddProperty("Affinity.Enabled", GetType(), Settings.EnablePlugin);
            pluginManager.AddProperty("Affinity.IsGameRunning", GetType(), false);
            pluginManager.AddProperty("Affinity.GameName", GetType(), string.Empty);
            pluginManager.AddProperty("Affinity.TrackName", GetType(), string.Empty);
            pluginManager.AddProperty("Affinity.CarModel", GetType(), string.Empty);
            pluginManager.AddProperty("Affinity.CurrentContextDistanceKm", GetType(), 0.0);
            pluginManager.AddProperty("Affinity.CurrentContextDistanceMiles", GetType(), 0.0);
            pluginManager.AddProperty("Affinity.SessionDistanceKm", GetType(), 0.0);
            pluginManager.AddProperty("Affinity.SessionDistanceMiles", GetType(), 0.0);
            pluginManager.AddProperty("Affinity.DataFilePath", GetType(), _databasePath);

            RefreshDistanceSummaries();
            SimHub.Logging.Current.Info($"Affinity v{Version} - Initialised");
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            try
            {
                pluginManager.SetPropertyValue("Affinity.Enabled", GetType(), Settings.EnablePlugin);
                pluginManager.SetPropertyValue("Affinity.IsGameRunning", GetType(), data.GameRunning);
                pluginManager.SetPropertyValue("Affinity.DataFilePath", GetType(), _databasePath);

                if (!Settings.EnablePlugin || !data.GameRunning || data.NewData == null)
                {
                    DataStatus = !Settings.EnablePlugin ? "Plugin disabled" : "Waiting for telemetry";
                    ResetActiveSession(clearContext: false);
                    PublishProperties(pluginManager, string.Empty, string.Empty, string.Empty, 0.0, 0.0);
                    _hasLoggedDataError = false;
                    return;
                }

                string gameName = NormalizeContextValue(data.GameName, "Unknown Game");
                string carModel = NormalizeContextValue(data.NewData.CarModel, "Unknown Car");
                string trackName = NormalizeContextValue(data.NewData.TrackName, "Unknown Track");
                string trackNameWithConfig = NormalizeContextValue(data.NewData.TrackNameWithConfig, trackName);

                CurrentGameName = gameName;
                CurrentCarModel = carModel;
                CurrentTrackName = trackName;
                CurrentTrackNameWithConfig = trackNameWithConfig;

                string contextKey = BuildContextKey(gameName, carModel, trackNameWithConfig);
                Guid sessionId = data.SessionId;
                double sessionMeters = GetSessionDistanceMeters(data.NewData);

                if (!string.Equals(_activeContextKey, contextKey, StringComparison.OrdinalIgnoreCase) ||
                    _activeSessionId != sessionId ||
                    data.NewData.IsSessionRestart)
                {
                    _activeContextKey = contextKey;
                    _activeSessionId = sessionId;
                    _lastObservedSessionMeters = sessionMeters;
                    SessionDistanceKm = sessionMeters / MetersPerKilometer;
                    DataStatus = "Tracking session distance";
                }
                else if (sessionMeters >= 0.0)
                {
                    double deltaMeters = sessionMeters - _lastObservedSessionMeters;
                    if (deltaMeters > 0.0)
                    {
                        TrackBucket bucket = GetOrCreateTrackBucket(gameName, carModel, trackName, trackNameWithConfig);
                        bucket.TotalDistanceMeters += deltaMeters;
                        bucket.LastUpdatedUtc = DateTime.UtcNow;
                        _pendingMetersSinceSave += deltaMeters;
                        _lastObservedSessionMeters = sessionMeters;
                        SessionDistanceKm = sessionMeters / MetersPerKilometer;
                        CurrentContextDistanceKm = bucket.TotalDistanceMeters / MetersPerKilometer;
                        DataStatus = $"Recorded {CurrentContextDistanceKm:F2} km for {CurrentContext}";

                        if (_pendingMetersSinceSave >= SaveThresholdMeters)
                        {
                            SaveDatabase();
                            RefreshDistanceSummaries();
                            _pendingMetersSinceSave = 0.0;
                        }
                    }
                    else if (sessionMeters + 1.0 < _lastObservedSessionMeters)
                    {
                        _lastObservedSessionMeters = sessionMeters;
                        SessionDistanceKm = sessionMeters / MetersPerKilometer;
                        DataStatus = "Session distance reset detected";
                    }
                }

                TrackBucket currentBucket = GetOrCreateTrackBucket(gameName, carModel, trackName, trackNameWithConfig);
                CurrentContextDistanceKm = currentBucket.TotalDistanceMeters / MetersPerKilometer;
                PublishProperties(pluginManager, gameName, GetDisplayTrackNameWithConfig(gameName, trackNameWithConfig), carModel, CurrentContextDistanceKm, SessionDistanceKm);
                _hasLoggedDataError = false;
            }
            catch (Exception ex)
            {
                if (_hasLoggedDataError)
                {
                    return;
                }

                SimHub.Logging.Current.Error($"Affinity - DataUpdate error: {ex}");
                _hasLoggedDataError = true;
            }
        }

        public void End(PluginManager pluginManager)
        {
            SaveDatabase();
            SaveSettings();
            SimHub.Logging.Current.Info("Affinity - Shutting down");
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

        internal void SaveSettings()
        {
            try
            {
                string directory = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonConvert.SerializeObject(Settings, Formatting.Indented);
                File.WriteAllText(_settingsPath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error($"Affinity - Failed to save settings: {ex.Message}");
            }
        }

        internal void ResetSettings()
        {
            Settings.Reset();
            SaveSettings();
            OnPropertyChanged(nameof(Settings));
            RefreshDistanceSummaries();
            NotifyDistanceDisplayChanged();
        }

        internal void RefreshDisplaySettings()
        {
            RefreshDistanceSummaries();
            NotifyDistanceDisplayChanged();
        }

        internal void RefreshDistanceSummaries()
        {
            List<DistanceSummary> summaries = BuildDistanceSummaries()
                .OrderBy(summary => summary.GameName)
                .ThenBy(summary => summary.CarModel)
                .ThenBy(summary => summary.TrackNameWithConfig)
                .ToList();

            List<GameDistanceTab> tabs = summaries
                .GroupBy(summary => summary.GameName)
                .Select(group => new GameDistanceTab
                {
                    GameName = group.Key,
                    TrackSummaries = group
                        .GroupBy(summary => summary.TrackNameWithConfig)
                        .Select(trackGroup => new TrackDistanceSummary
                        {
                            TrackName = trackGroup.Key,
                            TrackDisplayName = GetDisplayTrackNameWithConfig(group.Key, trackGroup.Key),
                            DistanceKm = trackGroup.Sum(summary => summary.TotalDistanceKm),
                            DistanceMiles = trackGroup.Sum(summary => summary.TotalDistanceMiles),
                            DistanceDisplay = Settings.DisplayInMiles
                                ? trackGroup.Sum(summary => summary.TotalDistanceMiles)
                                : trackGroup.Sum(summary => summary.TotalDistanceKm)
                        })
                        .OrderByDescending(summary => summary.DistanceDisplay)
                        .ThenBy(summary => summary.TrackName)
                        .ToList(),
                    CarSummaries = group
                        .GroupBy(summary => summary.CarModel)
                        .Select(carGroup => new CarDistanceSummary
                        {
                            CarModel = carGroup.Key,
                            DistanceKm = carGroup.Sum(summary => summary.TotalDistanceKm),
                            DistanceMiles = carGroup.Sum(summary => summary.TotalDistanceMiles),
                            DistanceDisplay = Settings.DisplayInMiles
                                ? carGroup.Sum(summary => summary.TotalDistanceMiles)
                                : carGroup.Sum(summary => summary.TotalDistanceKm)
                        })
                        .OrderByDescending(summary => summary.DistanceDisplay)
                        .ThenBy(summary => summary.CarModel)
                        .ToList()
                })
                .OrderBy(tab => tab.GameName)
                .ToList();

            string selectedGame = SelectedGameTab?.GameName;

            GameTabs.Clear();
            foreach (GameDistanceTab tab in tabs)
            {
                GameTabs.Add(tab);
            }

            SelectedGameTab = GameTabs.FirstOrDefault(tab =>
                string.Equals(tab.GameName, selectedGame, StringComparison.OrdinalIgnoreCase))
                ?? GameTabs.FirstOrDefault();
        }

        internal void ClearAllData()
        {
            _database = new AffinityDatabase();
            SaveDatabase();
            RefreshDistanceSummaries();
            ResetActiveSession(clearContext: false);
            CurrentContextDistanceKm = 0.0;
            SessionDistanceKm = 0.0;
            DataStatus = "Cleared all stored affinity data";
        }

        private AffinitySettings LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    return new AffinitySettings();
                }

                string json = File.ReadAllText(_settingsPath, Encoding.UTF8);
                return JsonConvert.DeserializeObject<AffinitySettings>(json) ?? new AffinitySettings();
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn($"Affinity - Failed to load settings, using defaults: {ex.Message}");
                return new AffinitySettings();
            }
        }

        private AffinityDatabase LoadDatabase()
        {
            try
            {
                if (!File.Exists(_databasePath))
                {
                    return new AffinityDatabase();
                }

                string json = File.ReadAllText(_databasePath, Encoding.UTF8);
                return JsonConvert.DeserializeObject<AffinityDatabase>(json) ?? new AffinityDatabase();
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn($"Affinity - Failed to load database, using empty store: {ex.Message}");
                return new AffinityDatabase();
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
                SimHub.Logging.Current.Warn($"Affinity - Failed to load AC track map: {ex.Message}");
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void SaveDatabase()
        {
            try
            {
                string directory = Path.GetDirectoryName(_databasePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonConvert.SerializeObject(_database, Formatting.Indented);
                File.WriteAllText(_databasePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error($"Affinity - Failed to save database: {ex.Message}");
            }
        }

        private IEnumerable<DistanceSummary> BuildDistanceSummaries()
        {
            foreach (KeyValuePair<string, GameBucket> gameEntry in _database.Games)
            {
                foreach (KeyValuePair<string, CarBucket> carEntry in gameEntry.Value.Cars)
                {
                    foreach (KeyValuePair<string, TrackBucket> trackEntry in carEntry.Value.Tracks)
                    {
                        TrackBucket track = trackEntry.Value;
                        yield return new DistanceSummary
                        {
                            GameName = gameEntry.Key,
                            CarModel = carEntry.Key,
                            TrackName = track.TrackName,
                            TrackNameWithConfig = track.TrackNameWithConfig,
                            TotalDistanceKm = track.TotalDistanceMeters / MetersPerKilometer,
                            TotalDistanceMiles = track.TotalDistanceMeters / MetersPerMile,
                            LastUpdatedUtc = track.LastUpdatedUtc
                        };
                    }
                }
            }
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

        private double GetSessionDistanceMeters(StatusDataBase status)
        {
            if (status == null)
            {
                return 0.0;
            }

            double trackLengthMeters = status.TrackLength > 0.0 ? status.TrackLength : status.ReportedTrackLength;
            if (trackLengthMeters > 0.0)
            {
                double trackPositionMeters = status.TrackPositionMeters;
                if (trackPositionMeters < 0.0 && status.TrackPositionPercent > 0.0)
                {
                    double trackPositionPercent = status.TrackPositionPercent > 1.0 && status.TrackPositionPercent <= 100.0
                        ? status.TrackPositionPercent / 100.0
                        : status.TrackPositionPercent;
                    trackPositionMeters = trackPositionPercent * trackLengthMeters;
                }

                trackPositionMeters = Math.Max(0.0, Math.Min(trackPositionMeters, trackLengthMeters));
                return Math.Max(0, status.CompletedLaps) * trackLengthMeters + trackPositionMeters;
            }

            if (status.SessionOdo > 0.0)
            {
                return status.SessionOdo * MetersPerKilometer;
            }

            return 0.0;
        }

        private void PublishProperties(PluginManager pluginManager, string gameName, string trackName, string carModel, double totalKm, double sessionKm)
        {
            pluginManager.SetPropertyValue("Affinity.GameName", GetType(), gameName);
            pluginManager.SetPropertyValue("Affinity.TrackName", GetType(), trackName);
            pluginManager.SetPropertyValue("Affinity.CarModel", GetType(), carModel);
            pluginManager.SetPropertyValue("Affinity.CurrentContextDistanceKm", GetType(), totalKm);
            pluginManager.SetPropertyValue("Affinity.CurrentContextDistanceMiles", GetType(), totalKm * MetersPerKilometer / MetersPerMile);
            pluginManager.SetPropertyValue("Affinity.SessionDistanceKm", GetType(), sessionKm);
            pluginManager.SetPropertyValue("Affinity.SessionDistanceMiles", GetType(), sessionKm * MetersPerKilometer / MetersPerMile);
        }

        private void ResetActiveSession(bool clearContext)
        {
            _activeSessionId = Guid.Empty;
            _activeContextKey = string.Empty;
            _lastObservedSessionMeters = -1.0;
            _pendingMetersSinceSave = 0.0;
            SessionDistanceKm = 0.0;
            CurrentContextDistanceKm = clearContext ? 0.0 : CurrentContextDistanceKm;
        }

        private static string BuildContextKey(string gameName, string carModel, string trackNameWithConfig)
        {
            return $"{gameName}|{carModel}|{trackNameWithConfig}";
        }

        private bool IsAssettoCorsaGame(string gameName)
        {
            return string.Equals(NormalizeGameName(gameName), "assettocorsa", StringComparison.Ordinal);
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

        private static string NormalizeContextValue(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
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

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void NotifyDistanceDisplayChanged()
        {
            OnPropertyChanged(nameof(DistanceUnitLabel));
            OnPropertyChanged(nameof(DistanceColumnHeader));
            OnPropertyChanged(nameof(CurrentContextDistanceDisplay));
            OnPropertyChanged(nameof(SessionDistanceDisplay));
        }
    }
}
