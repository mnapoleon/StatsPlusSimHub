using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
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
        private const string LiteDbDataFileName = "StatsPlus.laps.ldb";
        private const string Version = "0.2.0";

        private bool _hasLoggedDataError;
        private string _settingsPath = string.Empty;
        private string _databasePath = string.Empty;
        private string _acTrackMapPath = string.Empty;
        private StatsPlusLiteDbRepository _liteDbRepository;
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
        private double _pendingObservedLastLapSeconds = -1.0;
        private bool _pendingLastLapTimeNeedsRefresh;
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
            : $"{SelectedTrackSummary.GameName} / {SelectedTrackSummary.CarModelDisplay} / {SelectedTrackSummary.TrackNameWithConfigDisplay}";

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
            InitializeStoragePaths(pluginManager);
            _acTrackMapPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ac_track_id_map.json");
            Settings = LoadSettings();
            _assettoCorsaTrackMap = LoadAssettoCorsaTrackMap();
            InitializeDatabase();

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
            _liteDbRepository?.Dispose();
            _liteDbRepository = null;
            BackupDatabaseFile();
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

        private bool HasLapRepository => _liteDbRepository != null;

        internal static string GetStatsPlusStorageRoot(string commonStorageRoot)
        {
            if (string.IsNullOrWhiteSpace(commonStorageRoot))
            {
                return Path.Combine("PluginsData", "StatsPlus");
            }

            string trimmedPath = commonStorageRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string pluginsDataRoot = Directory.GetParent(trimmedPath)?.FullName ?? trimmedPath;
            return Path.Combine(pluginsDataRoot, "StatsPlus");
        }

        internal static void MigrateFileIfNeeded(string targetPath, params string[] candidatePaths)
        {
            if (string.IsNullOrWhiteSpace(targetPath) || File.Exists(targetPath) || candidatePaths == null)
            {
                return;
            }

            string directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            foreach (string candidatePath in candidatePaths)
            {
                if (string.IsNullOrWhiteSpace(candidatePath) || !File.Exists(candidatePath))
                {
                    continue;
                }

                File.Move(candidatePath, targetPath);
                return;
            }
        }

        internal static void BackupFileIfPresent(string sourcePath, string backupPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) ||
                string.IsNullOrWhiteSpace(backupPath) ||
                !File.Exists(sourcePath))
            {
                return;
            }

            string directory = Path.GetDirectoryName(backupPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(sourcePath, backupPath, overwrite: true);
        }

        private void InitializeDatabase()
        {
            try
            {
                _liteDbRepository = new StatsPlusLiteDbRepository(_databasePath);
                _liteDbRepository.Initialize();
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error($"StatsPlus - Failed to initialize LiteDB database: {ex}");
                _liteDbRepository?.Dispose();
                _liteDbRepository = null;
            }
        }

        private void InitializeStoragePaths(PluginManager pluginManager)
        {
            string commonSettingsPath = pluginManager.GetCommonStoragePath(SettingsFileName);
            string commonStorageRoot = Path.GetDirectoryName(commonSettingsPath) ?? string.Empty;
            string statsPlusStorageRoot = GetStatsPlusStorageRoot(commonStorageRoot);
            string commonStatsPlusStorageRoot = Path.Combine(commonStorageRoot, "StatsPlus");

            _settingsPath = Path.Combine(statsPlusStorageRoot, SettingsFileName);
            _databasePath = Path.Combine(statsPlusStorageRoot, LiteDbDataFileName);

            TryMigrateStorageFile(
                _settingsPath,
                Path.Combine(commonStatsPlusStorageRoot, SettingsFileName),
                commonSettingsPath);
            TryMigrateStorageFile(
                _databasePath,
                Path.Combine(commonStatsPlusStorageRoot, LiteDbDataFileName),
                pluginManager.GetCommonStoragePath(LiteDbDataFileName));
        }

        private void TryMigrateStorageFile(string targetPath, params string[] candidatePaths)
        {
            try
            {
                MigrateFileIfNeeded(targetPath, candidatePaths);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn($"StatsPlus - Failed to migrate storage file to {targetPath}: {ex.Message}");
            }
        }

        private void BackupDatabaseFile()
        {
            try
            {
                BackupFileIfPresent(_databasePath, _databasePath + ".bak");
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn($"StatsPlus - Failed to back up database: {ex.Message}");
            }
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

            if (HasLapRepository)
            {
                _liteDbRepository.ToggleLapValidity(SelectedLap.LapId);
            }
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

            if (HasLapRepository)
            {
                _liteDbRepository.DeleteGameData(SelectedGameHistoryTab.GameName);
            }

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
            if (HasLapRepository)
            {
                _liteDbRepository.ClearAllData();
            }

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
                if (data.OldData.CompletedLaps == 0 &&
                    data.NewData.CompletedLaps == 1 &&
                    SessionLapCount == 0 &&
                    ToSeconds(data.NewData.LastLapTime) <= 0)
                {
                    _pendingLapCapture = false;
                    _pendingCompletedLapCount = -1;
                    _pendingObservedLastLapSeconds = -1.0;
                    _pendingLastLapTimeNeedsRefresh = false;
                    return;
                }

                _pendingLapCapture = true;
                _pendingCompletedLapCount = data.NewData.CompletedLaps;
                _pendingObservedLastLapSeconds = ToSeconds(data.NewData.LastLapTime);
                double previousLastLapSeconds = ToSeconds(data.OldData.LastLapTime);
                _pendingLastLapTimeNeedsRefresh = _pendingObservedLastLapSeconds <= 0 ||
                    AreClose(_pendingObservedLastLapSeconds, previousLastLapSeconds);
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

            if (_pendingLastLapTimeNeedsRefresh && AreClose(lapTime, _pendingObservedLastLapSeconds))
            {
                return;
            }

            if (_pendingLastLapTimeNeedsRefresh &&
                _pendingObservedLastLapSeconds <= 0 &&
                !_capturedSector1 &&
                !_capturedSector2 &&
                LastLapSeconds > 0 &&
                AreClose(lapTime, LastLapSeconds))
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
            _pendingObservedLastLapSeconds = -1.0;
            _pendingLastLapTimeNeedsRefresh = false;
            _capturedSector1Seconds = 0.0;
            _capturedSector2Seconds = 0.0;
            _capturedSector1 = false;
            _capturedSector2 = false;

            PublishLapProperties(pluginManager);
            RefreshStoredTrackSummaries();
        }

        private void AddLapToDatabase(string gameName, string carModel, string trackName, string trackNameWithConfig, RecordedLap lap)
        {
            if (HasLapRepository)
            {
                _liteDbRepository.AddLap(gameName, carModel, trackName, trackNameWithConfig, lap);
            }

            RefreshPersonalBestProperties(PluginManager);
        }

        private double GetBestLapSeconds(string gameName, string carModel, string trackNameWithConfig)
        {
            return HasLapRepository
                ? _liteDbRepository.GetBestLapSeconds(gameName, carModel, trackNameWithConfig)
                : 0.0;
        }

        private IEnumerable<StoredTrackSummary> BuildTrackSummaries()
        {
            if (HasLapRepository)
            {
                foreach (StoredTrackSummary summary in _liteDbRepository.GetTrackSummaries())
                {
                    summary.CarModelDisplay = ResolveCarDisplayName(summary.GameName, summary.CarModel, summary.CarModelDisplay);
                    summary.TrackNameWithConfigDisplay = ResolveTrackDisplayName(summary.GameName, summary.TrackNameWithConfig, summary.TrackNameWithConfigDisplay);
                    yield return summary;
                }

                yield break;
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
            return HasLapRepository
                ? _liteDbRepository.GetPersonalBestPropertyValues()
                : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
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

        internal static string BuildPersonalBestPropertyName(string gameName, string carModel, string trackVariation)
        {
            return StatsPlusPropertyNames.BuildPersonalBestPropertyName(gameName, carModel, trackVariation);
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

        private static bool AreClose(double left, double right)
        {
            return Math.Abs(left - right) < 0.0001;
        }

        private static double ToSeconds(TimeSpan? value)
        {
            return value.HasValue ? value.Value.TotalSeconds : 0.0;
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
                case "lmu":
                case "lemansultimate":
                    return Settings.RecordLeMansUltimate;
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

        private void LoadSelectedTrackLaps(DateTime? selectedTimestamp = null)
        {
            List<RecordedLapView> lapViews = new List<RecordedLapView>();

            if (HasLapRepository && SelectedTrackSummary != null)
            {
                lapViews = _liteDbRepository.GetTrackLaps(
                    SelectedTrackSummary.GameName,
                    SelectedTrackSummary.CarModel,
                    SelectedTrackSummary.TrackNameWithConfig);

                foreach (RecordedLapView lapView in lapViews)
                {
                    lapView.CarModelDisplay = ResolveCarDisplayName(lapView.GameName, lapView.CarModel, lapView.CarModelDisplay);
                    lapView.TrackNameWithConfigDisplay = ResolveTrackDisplayName(lapView.GameName, lapView.TrackNameWithConfig, lapView.TrackNameWithConfigDisplay);
                }
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

        private static string ResolveCarDisplayName(string gameName, string rawCarModel, string displayCarModel)
        {
            return string.IsNullOrWhiteSpace(displayCarModel) ? rawCarModel : displayCarModel;
        }

        private string ResolveTrackDisplayName(string gameName, string rawTrackNameWithConfig, string displayTrackNameWithConfig)
        {
            return string.IsNullOrWhiteSpace(displayTrackNameWithConfig)
                ? GetDisplayTrackNameWithConfig(gameName, rawTrackNameWithConfig)
                : displayTrackNameWithConfig;
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
