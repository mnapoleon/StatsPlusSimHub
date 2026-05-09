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
        private enum SessionDistanceSource
        {
            Unknown = 0,
            Derived = 1,
            SessionOdoMeters = 2,
            SessionOdoKilometers = 3
        }

        private const string SettingsFileName = "Affinity.settings.json";
        private const string DataFileName = "Affinity.distance.json";
        private const string DebugLogFileName = "Affinity.distance.debug.log";
        private const string Version = "0.1.0";
        private const double MetersPerKilometer = 1000.0;
        private const double MetersPerMile = 1609.344;
        private const double SaveThresholdMeters = 50.0;

        private bool _hasLoggedDataError;
        private string _settingsPath = string.Empty;
        private string _databasePath = string.Empty;
        private string _debugLogPath = string.Empty;
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
        private int _currentContextCompletedLaps;
        private int _sessionCompletedLaps;
        private double _totalDistanceKm;
        private bool _isTelemetryActive;
        private GameDistanceTab _selectedGameTab;
        private Guid _activeSessionId = Guid.Empty;
        private string _activeContextKey = string.Empty;
        private SessionDistanceSource _sessionDistanceSource = SessionDistanceSource.Unknown;
        private double _sessionStartTrackPositionMeters = -1.0;
        private double _sessionStatefulAbsoluteMeters;
        private double _lastTrackPositionWithinLapMeters = -1.0;
        private double _sessionDistanceOriginMeters;
        private double _lastObservedSessionMeters = -1.0;
        private int _lastObservedCompletedLaps = -1;
        private double _pendingMetersSinceSave;
        private DateTime _lastTelemetryDebugLogUtc = DateTime.MinValue;

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

        public double TotalDistanceDisplay => Settings.DisplayInMiles
            ? TotalDistanceKm * MetersPerKilometer / MetersPerMile
            : TotalDistanceKm;

        public Brush StatusSectionForeground => IsTelemetryActive ? Brushes.LimeGreen : Brushes.Red;

        public int CurrentContextCompletedLaps
        {
            get => _currentContextCompletedLaps;
            private set
            {
                if (_currentContextCompletedLaps == value)
                {
                    return;
                }

                _currentContextCompletedLaps = value;
                OnPropertyChanged();
            }
        }

        public int SessionCompletedLaps
        {
            get => _sessionCompletedLaps;
            private set
            {
                if (_sessionCompletedLaps == value)
                {
                    return;
                }

                _sessionCompletedLaps = value;
                OnPropertyChanged();
            }
        }

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

        public double TotalDistanceKm
        {
            get => _totalDistanceKm;
            private set
            {
                if (Math.Abs(_totalDistanceKm - value) < 0.0001)
                {
                    return;
                }

                _totalDistanceKm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalDistanceDisplay));
            }
        }

        public bool IsTelemetryActive
        {
            get => _isTelemetryActive;
            private set
            {
                if (_isTelemetryActive == value)
                {
                    return;
                }

                _isTelemetryActive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusSectionForeground));
            }
        }

        public void Init(PluginManager pluginManager)
        {
            PluginManager = pluginManager;
            _settingsPath = pluginManager.GetCommonStoragePath(SettingsFileName);
            _databasePath = pluginManager.GetCommonStoragePath(DataFileName);
            _debugLogPath = pluginManager.GetCommonStoragePath(DebugLogFileName);
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
            pluginManager.AddProperty("Affinity.CurrentContextCompletedLaps", GetType(), 0);
            pluginManager.AddProperty("Affinity.SessionDistanceKm", GetType(), 0.0);
            pluginManager.AddProperty("Affinity.SessionDistanceMiles", GetType(), 0.0);
            pluginManager.AddProperty("Affinity.SessionCompletedLaps", GetType(), 0);
            pluginManager.AddProperty("Affinity.DataFilePath", GetType(), _databasePath);
            pluginManager.AddProperty("Affinity.DebugLogPath", GetType(), _debugLogPath);

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
                pluginManager.SetPropertyValue("Affinity.DebugLogPath", GetType(), _debugLogPath);

                if (!Settings.EnablePlugin || !data.GameRunning || data.NewData == null)
                {
                    DataStatus = !Settings.EnablePlugin ? "Plugin disabled" : "Waiting for telemetry";
                    IsTelemetryActive = false;
                    ResetActiveSession(clearContext: false);
                    PublishProperties(pluginManager, string.Empty, string.Empty, string.Empty, 0.0, 0, 0.0, 0);
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
                double absoluteSessionMeters = -1.0;
                int completedLaps = Math.Max(0, data.NewData.CompletedLaps);
                bool shouldDebugTelemetry = ShouldDebugTelemetry(gameName);

                if (!string.Equals(_activeContextKey, contextKey, StringComparison.OrdinalIgnoreCase) ||
                    _activeSessionId != sessionId ||
                    data.NewData.IsSessionRestart)
                {
                    _activeContextKey = contextKey;
                    _activeSessionId = sessionId;
                    _sessionDistanceSource = ResolveSessionDistanceSource(gameName, data.NewData);
                    _sessionStartTrackPositionMeters = GetSessionStartTrackPositionMeters(gameName, data.NewData);
                    _sessionStatefulAbsoluteMeters = 0.0;
                    _lastTrackPositionWithinLapMeters = GetTrackPositionWithinLapMeters(data.NewData, data.NewData.TrackLength > 0.0 ? data.NewData.TrackLength : data.NewData.ReportedTrackLength);
                    _sessionDistanceOriginMeters = ShouldUseZeroSessionOrigin(gameName, _sessionDistanceSource)
                        ? 0.0
                        : GetAbsoluteSessionDistanceMeters(gameName, data.NewData, _sessionDistanceSource);
                    _lastObservedSessionMeters = 0.0;
                    _lastObservedCompletedLaps = completedLaps;
                    SessionDistanceKm = 0.0;
                    SessionCompletedLaps = completedLaps;
                    DataStatus = "Tracking session distance and laps";
                    IsTelemetryActive = true;

                    if (shouldDebugTelemetry)
                    {
                        LogTelemetryDebugSnapshot("session-start", gameName, carModel, trackNameWithConfig, sessionId, data.NewData, -1.0, 0.0, 0, false);
                    }
                }
                else
                {
                    if (IsAutomobilista2Game(gameName) && _sessionDistanceSource == SessionDistanceSource.Derived)
                    {
                        double automobilista2TrackLengthMeters = data.NewData.TrackLength > 0.0 ? data.NewData.TrackLength : data.NewData.ReportedTrackLength;
                        absoluteSessionMeters = UpdateAutomobilista2AbsoluteSessionDistanceMeters(data.NewData, automobilista2TrackLengthMeters);
                    }
                    else
                    {
                        absoluteSessionMeters = GetAbsoluteSessionDistanceMeters(gameName, data.NewData, _sessionDistanceSource);
                    }

                    if (absoluteSessionMeters < 0.0)
                    {
                        return;
                    }

                    double sessionMeters = Math.Max(0.0, absoluteSessionMeters - _sessionDistanceOriginMeters);
                    double deltaMeters = sessionMeters - _lastObservedSessionMeters;
                    int lapDelta = completedLaps - _lastObservedCompletedLaps;
                    double trackLengthMeters = data.NewData.TrackLength > 0.0 ? data.NewData.TrackLength : data.NewData.ReportedTrackLength;
                    bool looksLikeDerivedLapBoundaryWrap = _sessionDistanceSource == SessionDistanceSource.Derived &&
                        trackLengthMeters > 0.0 &&
                        lapDelta == 0 &&
                        _lastObservedSessionMeters > 0.0 &&
                        sessionMeters + (trackLengthMeters * 0.75) < _lastObservedSessionMeters &&
                        sessionMeters + (trackLengthMeters * 1.25) > _lastObservedSessionMeters;
                    bool looksLikeInitialPositionSnap = deltaMeters > 0.0 &&
                        lapDelta == 0 &&
                        completedLaps == 0 &&
                        _lastObservedSessionMeters <= 25.0 &&
                        sessionMeters >= Math.Max(200.0, trackLengthMeters * 0.25) &&
                        !IsAutomobilista2Game(gameName) &&
                        data.NewData.SpeedKmh < 5.0;
                    TrackBucket bucket = GetOrCreateTrackBucket(gameName, carModel, trackName, trackNameWithConfig);
                    bool bucketUpdated = false;

                    if (looksLikeDerivedLapBoundaryWrap)
                    {
                        SessionDistanceKm = _lastObservedSessionMeters / MetersPerKilometer;
                        DataStatus = "Waiting for lap counter sync at line";
                        IsTelemetryActive = true;

                        if (shouldDebugTelemetry)
                        {
                            LogTelemetryDebugSnapshot("lap-wrap-wait", gameName, carModel, trackNameWithConfig, sessionId, data.NewData, deltaMeters, sessionMeters, lapDelta, false);
                        }
                    }
                    else if (looksLikeInitialPositionSnap)
                    {
                        _lastObservedSessionMeters = sessionMeters;
                        SessionDistanceKm = sessionMeters / MetersPerKilometer;
                        DataStatus = "Ignoring initial telemetry position snap";
                        IsTelemetryActive = true;

                        if (shouldDebugTelemetry)
                        {
                            LogTelemetryDebugSnapshot("initial-snap", gameName, carModel, trackNameWithConfig, sessionId, data.NewData, deltaMeters, sessionMeters, lapDelta, true);
                        }
                    }
                    else if (deltaMeters > 0.0)
                    {
                        bucket.TotalDistanceMeters += deltaMeters;
                        bucket.LastUpdatedUtc = DateTime.UtcNow;
                        _pendingMetersSinceSave += deltaMeters;
                        _lastObservedSessionMeters = sessionMeters;
                        SessionDistanceKm = sessionMeters / MetersPerKilometer;
                        bucketUpdated = true;

                        if (shouldDebugTelemetry && ShouldLogTelemetryProgress(deltaMeters, lapDelta, trackLengthMeters))
                        {
                            LogTelemetryDebugSnapshot("progress", gameName, carModel, trackNameWithConfig, sessionId, data.NewData, deltaMeters, sessionMeters, lapDelta, false);
                        }
                    }
                    else if (sessionMeters + 1.0 < _lastObservedSessionMeters)
                    {
                        _lastObservedSessionMeters = sessionMeters;
                        SessionDistanceKm = sessionMeters / MetersPerKilometer;
                        DataStatus = "Session distance reset detected";
                        IsTelemetryActive = true;

                        if (shouldDebugTelemetry)
                        {
                            LogTelemetryDebugSnapshot("distance-reset", gameName, carModel, trackNameWithConfig, sessionId, data.NewData, deltaMeters, sessionMeters, lapDelta, false);
                        }
                    }

                    if (lapDelta > 0)
                    {
                        bucket.CompletedLaps += lapDelta;
                        bucket.LastUpdatedUtc = DateTime.UtcNow;
                        _lastObservedCompletedLaps = completedLaps;
                        SessionCompletedLaps = completedLaps;
                        bucketUpdated = true;

                        if (shouldDebugTelemetry)
                        {
                            LogTelemetryDebugSnapshot("lap-change", gameName, carModel, trackNameWithConfig, sessionId, data.NewData, deltaMeters, sessionMeters, lapDelta, false);
                        }
                    }
                    else if (completedLaps < _lastObservedCompletedLaps)
                    {
                        _lastObservedCompletedLaps = completedLaps;
                        SessionCompletedLaps = completedLaps;
                        DataStatus = "Session lap counter reset detected";
                        IsTelemetryActive = true;

                        if (shouldDebugTelemetry)
                        {
                            LogTelemetryDebugSnapshot("lap-reset", gameName, carModel, trackNameWithConfig, sessionId, data.NewData, deltaMeters, sessionMeters, lapDelta, false);
                        }
                    }

                    if (bucketUpdated)
                    {
                        CurrentContextDistanceKm = bucket.TotalDistanceMeters / MetersPerKilometer;
                        CurrentContextCompletedLaps = bucket.CompletedLaps;
                        DataStatus = $"Recorded {CurrentContextDistanceKm:F2} km and {CurrentContextCompletedLaps} laps for {CurrentContext}";
                        IsTelemetryActive = true;

                        if (_pendingMetersSinceSave >= SaveThresholdMeters || lapDelta > 0)
                        {
                            SaveDatabase();
                            RefreshDistanceSummaries();
                            _pendingMetersSinceSave = 0.0;
                        }
                    }
                    else if (shouldDebugTelemetry && ShouldLogTelemetryHeartbeat())
                    {
                        LogTelemetryDebugSnapshot("heartbeat", gameName, carModel, trackNameWithConfig, sessionId, data.NewData, deltaMeters, sessionMeters, lapDelta, false);
                    }
                }

                TrackBucket currentBucket = GetOrCreateTrackBucket(gameName, carModel, trackName, trackNameWithConfig);
                CurrentContextDistanceKm = currentBucket.TotalDistanceMeters / MetersPerKilometer;
                CurrentContextCompletedLaps = currentBucket.CompletedLaps;
                IsTelemetryActive = true;
                PublishProperties(pluginManager, gameName, GetDisplayTrackNameWithConfig(gameName, trackNameWithConfig), carModel, CurrentContextDistanceKm, CurrentContextCompletedLaps, SessionDistanceKm, SessionCompletedLaps);
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
                    TotalDistanceKm = group.Sum(summary => summary.TotalDistanceKm),
                    TotalDistanceMiles = group.Sum(summary => summary.TotalDistanceMiles),
                    TotalDistanceDisplay = Settings.DisplayInMiles
                        ? group.Sum(summary => summary.TotalDistanceMiles)
                        : group.Sum(summary => summary.TotalDistanceKm),
                    TotalCompletedLaps = group.Sum(summary => summary.CompletedLaps),
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
                                : trackGroup.Sum(summary => summary.TotalDistanceKm),
                            CompletedLaps = trackGroup.Sum(summary => summary.CompletedLaps)
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
                                : carGroup.Sum(summary => summary.TotalDistanceKm),
                            CompletedLaps = carGroup.Sum(summary => summary.CompletedLaps)
                        })
                        .OrderByDescending(summary => summary.DistanceDisplay)
                        .ThenBy(summary => summary.CarModel)
                        .ToList()
                })
                .OrderBy(tab => tab.GameName)
                .ToList();

            TotalDistanceKm = summaries.Sum(summary => summary.TotalDistanceKm);

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
            CurrentContextCompletedLaps = 0;
            SessionDistanceKm = 0.0;
            SessionCompletedLaps = 0;
            DataStatus = "Cleared all stored affinity data";
            IsTelemetryActive = false;
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
                            CompletedLaps = track.CompletedLaps,
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

        private SessionDistanceSource ResolveSessionDistanceSource(string gameName, StatusDataBase status)
        {
            if (IsAssettoCorsaGame(gameName) || IsRaceRoomGame(gameName) || IsAutomobilista2Game(gameName))
            {
                return SessionDistanceSource.Derived;
            }

            double trackLengthMeters = status?.TrackLength > 0.0 ? status.TrackLength : status?.ReportedTrackLength ?? 0.0;
            double derivedSessionMeters = GetDerivedSessionDistanceMeters(status, trackLengthMeters);
            if (status?.SessionOdo > 0.0)
            {
                double sessionOdoMeters = status.SessionOdo;
                double sessionOdoKilometers = status.SessionOdo * MetersPerKilometer;
                if (derivedSessionMeters >= 0.0)
                {
                    return Math.Abs(sessionOdoMeters - derivedSessionMeters) <= Math.Abs(sessionOdoKilometers - derivedSessionMeters)
                        ? SessionDistanceSource.SessionOdoMeters
                        : SessionDistanceSource.SessionOdoKilometers;
                }

                return status.SessionOdo >= 100.0
                    ? SessionDistanceSource.SessionOdoMeters
                    : SessionDistanceSource.SessionOdoKilometers;
            }

            return derivedSessionMeters >= 0.0
                ? SessionDistanceSource.Derived
                : SessionDistanceSource.Unknown;
        }

        private double GetAbsoluteSessionDistanceMeters(string gameName, StatusDataBase status, SessionDistanceSource source)
        {
            if (status == null)
            {
                return -1.0;
            }

            double trackLengthMeters = status.TrackLength > 0.0 ? status.TrackLength : status.ReportedTrackLength;
            switch (source)
            {
                case SessionDistanceSource.Derived:
                    if (IsAutomobilista2Game(gameName))
                    {
                        return _sessionStatefulAbsoluteMeters;
                    }

                    return GetDerivedSessionDistanceMeters(status, trackLengthMeters);
                case SessionDistanceSource.SessionOdoMeters:
                    return status.SessionOdo > 0.0 ? status.SessionOdo : -1.0;
                case SessionDistanceSource.SessionOdoKilometers:
                    return status.SessionOdo > 0.0 ? status.SessionOdo * MetersPerKilometer : -1.0;
                default:
                    return -1.0;
            }
        }

        private bool ShouldUseZeroSessionOrigin(string gameName, SessionDistanceSource source)
        {
            return source == SessionDistanceSource.Derived && IsAssettoCorsaGame(gameName);
        }

        private bool ShouldDebugTelemetry(string gameName)
        {
            string normalized = NormalizeGameName(gameName);
            return string.Equals(normalized, "assettocorsaevo", StringComparison.Ordinal) ||
                string.Equals(normalized, "automobilista2", StringComparison.Ordinal) ||
                string.Equals(normalized, "raceroomracingexperience", StringComparison.Ordinal) ||
                string.Equals(normalized, "r3e", StringComparison.Ordinal) ||
                string.Equals(normalized, "rrre", StringComparison.Ordinal);
        }

        private bool ShouldLogTelemetryProgress(double deltaMeters, int lapDelta, double trackLengthMeters)
        {
            if (lapDelta != 0)
            {
                return true;
            }

            if (deltaMeters >= Math.Max(100.0, trackLengthMeters * 0.25))
            {
                return true;
            }

            return ShouldLogTelemetryHeartbeat();
        }

        private bool ShouldLogTelemetryHeartbeat()
        {
            DateTime now = DateTime.UtcNow;
            if ((now - _lastTelemetryDebugLogUtc).TotalSeconds < 1.0)
            {
                return false;
            }

            _lastTelemetryDebugLogUtc = now;
            return true;
        }

        private void LogTelemetryDebugSnapshot(string reason, string gameName, string carModel, string trackNameWithConfig, Guid sessionId, StatusDataBase status, double deltaMeters, double sessionMeters, int lapDelta, bool looksLikeInitialPositionSnap)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_debugLogPath) || status == null)
                {
                    return;
                }

                string directory = Path.GetDirectoryName(_debugLogPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                double trackLengthMeters = status.TrackLength > 0.0 ? status.TrackLength : status.ReportedTrackLength;
                double derivedSessionMeters = GetDerivedSessionDistanceMeters(status, trackLengthMeters);
                double sessionOdoMeters = status.SessionOdo > 0.0 ? status.SessionOdo : -1.0;
                double sessionOdoKilometers = status.SessionOdo > 0.0 ? status.SessionOdo * MetersPerKilometer : -1.0;
                double absoluteSessionMeters = GetAbsoluteSessionDistanceMeters(gameName, status, _sessionDistanceSource);
                string line = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0:o} reason={1} game=\"{2}\" car=\"{3}\" track=\"{4}\" sessionId={5} source={6} originM={7:F2} absM={8:F2} sessM={9:F2} deltaM={10:F2} completedLaps={11} lapDelta={12} trackLenM={13:F2} reportedTrackLenM={14:F2} posM={15:F2} posPct={16:F5} sessOdoRaw={17:F5} sessOdoAsM={18:F2} sessOdoAsKmM={19:F2} derivedM={20:F2} speedKmh={21:F2} isRestart={22} initialSnap={23}",
                    DateTime.UtcNow,
                    reason,
                    gameName,
                    carModel,
                    trackNameWithConfig,
                    sessionId,
                    _sessionDistanceSource,
                    _sessionDistanceOriginMeters,
                    absoluteSessionMeters,
                    sessionMeters,
                    deltaMeters,
                    Math.Max(0, status.CompletedLaps),
                    lapDelta,
                    status.TrackLength,
                    status.ReportedTrackLength,
                    status.TrackPositionMeters,
                    status.TrackPositionPercent,
                    status.SessionOdo,
                    sessionOdoMeters,
                    sessionOdoKilometers,
                    derivedSessionMeters,
                    status.SpeedKmh,
                    status.IsSessionRestart,
                    looksLikeInitialPositionSnap);

                File.AppendAllText(_debugLogPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn($"Affinity - Failed to write debug telemetry log: {ex.Message}");
            }
        }

        private double UpdateAutomobilista2AbsoluteSessionDistanceMeters(StatusDataBase status, double trackLengthMeters)
        {
            if (status == null || trackLengthMeters <= 0.0)
            {
                return -1.0;
            }

            double trackPositionMeters = GetTrackPositionWithinLapMeters(status, trackLengthMeters);
            if (trackPositionMeters < 0.0)
            {
                return _sessionStatefulAbsoluteMeters;
            }

            if (_lastTrackPositionWithinLapMeters < 0.0)
            {
                _lastTrackPositionWithinLapMeters = trackPositionMeters;
                return _sessionStatefulAbsoluteMeters;
            }

            double deltaTrackPositionMeters = trackPositionMeters - _lastTrackPositionWithinLapMeters;
            if (deltaTrackPositionMeters < -(trackLengthMeters * 0.5))
            {
                deltaTrackPositionMeters += trackLengthMeters;
            }
            else if (deltaTrackPositionMeters > trackLengthMeters * 0.5)
            {
                deltaTrackPositionMeters -= trackLengthMeters;
            }

            if (deltaTrackPositionMeters > 0.0)
            {
                _sessionStatefulAbsoluteMeters += deltaTrackPositionMeters;
            }

            _lastTrackPositionWithinLapMeters = trackPositionMeters;
            return _sessionStatefulAbsoluteMeters;
        }

        private static double GetDerivedSessionDistanceMeters(StatusDataBase status, double trackLengthMeters)
        {
            if (status == null || trackLengthMeters <= 0.0)
            {
                return -1.0;
            }

            double trackPositionMeters = GetTrackPositionWithinLapMeters(status, trackLengthMeters);
            if (trackPositionMeters < 0.0)
            {
                return -1.0;
            }

            return Math.Max(0, status.CompletedLaps) * trackLengthMeters + trackPositionMeters;
        }

        private double GetSessionStartTrackPositionMeters(string gameName, StatusDataBase status)
        {
            if (!IsAutomobilista2Game(gameName) || status == null)
            {
                return -1.0;
            }

            double trackLengthMeters = status.TrackLength > 0.0 ? status.TrackLength : status.ReportedTrackLength;
            return GetTrackPositionWithinLapMeters(status, trackLengthMeters);
        }

        private static double GetTrackPositionWithinLapMeters(StatusDataBase status, double trackLengthMeters)
        {
            if (status == null || trackLengthMeters <= 0.0)
            {
                return -1.0;
            }

            double trackPositionMeters = status.TrackPositionMeters;
            if (trackPositionMeters > trackLengthMeters + 1.0)
            {
                return trackPositionMeters;
            }

            if (trackPositionMeters < 0.0 && status.TrackPositionPercent > 0.0)
            {
                double trackPositionPercent = status.TrackPositionPercent > 1.0 && status.TrackPositionPercent <= 100.0
                    ? status.TrackPositionPercent / 100.0
                    : status.TrackPositionPercent;
                trackPositionMeters = trackPositionPercent * trackLengthMeters;
            }

            return Math.Max(0.0, Math.Min(trackPositionMeters, trackLengthMeters));
        }

        private void PublishProperties(PluginManager pluginManager, string gameName, string trackName, string carModel, double totalKm, int totalCompletedLaps, double sessionKm, int sessionCompletedLaps)
        {
            pluginManager.SetPropertyValue("Affinity.GameName", GetType(), gameName);
            pluginManager.SetPropertyValue("Affinity.TrackName", GetType(), trackName);
            pluginManager.SetPropertyValue("Affinity.CarModel", GetType(), carModel);
            pluginManager.SetPropertyValue("Affinity.CurrentContextDistanceKm", GetType(), totalKm);
            pluginManager.SetPropertyValue("Affinity.CurrentContextDistanceMiles", GetType(), totalKm * MetersPerKilometer / MetersPerMile);
            pluginManager.SetPropertyValue("Affinity.CurrentContextCompletedLaps", GetType(), totalCompletedLaps);
            pluginManager.SetPropertyValue("Affinity.SessionDistanceKm", GetType(), sessionKm);
            pluginManager.SetPropertyValue("Affinity.SessionDistanceMiles", GetType(), sessionKm * MetersPerKilometer / MetersPerMile);
            pluginManager.SetPropertyValue("Affinity.SessionCompletedLaps", GetType(), sessionCompletedLaps);
        }

        private void ResetActiveSession(bool clearContext)
        {
            _activeSessionId = Guid.Empty;
            _activeContextKey = string.Empty;
            _sessionDistanceSource = SessionDistanceSource.Unknown;
            _sessionStartTrackPositionMeters = -1.0;
            _sessionStatefulAbsoluteMeters = 0.0;
            _lastTrackPositionWithinLapMeters = -1.0;
            _sessionDistanceOriginMeters = 0.0;
            _lastObservedSessionMeters = -1.0;
            _lastObservedCompletedLaps = -1;
            _pendingMetersSinceSave = 0.0;
            SessionDistanceKm = 0.0;
            SessionCompletedLaps = 0;
            CurrentContextCompletedLaps = clearContext ? 0 : CurrentContextCompletedLaps;
            CurrentContextDistanceKm = clearContext ? 0.0 : CurrentContextDistanceKm;
        }

        private static string BuildContextKey(string gameName, string carModel, string trackNameWithConfig)
        {
            return $"{gameName}|{carModel}|{trackNameWithConfig}";
        }

        private bool IsAssettoCorsaGame(string gameName)
        {
            string normalized = NormalizeGameName(gameName);
            return string.Equals(normalized, "assettocorsa", StringComparison.Ordinal) ||
                string.Equals(normalized, "assettocorsaevo", StringComparison.Ordinal);
        }

        private bool IsRaceRoomGame(string gameName)
        {
            string normalized = NormalizeGameName(gameName);
            return string.Equals(normalized, "raceroomracingexperience", StringComparison.Ordinal) ||
                string.Equals(normalized, "r3e", StringComparison.Ordinal) ||
                string.Equals(normalized, "rrre", StringComparison.Ordinal);
        }

        private bool IsAutomobilista2Game(string gameName)
        {
            string normalized = NormalizeGameName(gameName);
            return string.Equals(normalized, "automobilista2", StringComparison.Ordinal);
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
            OnPropertyChanged(nameof(TotalDistanceDisplay));
        }
    }
}
