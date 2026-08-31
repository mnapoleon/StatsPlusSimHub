using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StatsPlus
{
    public class GameDebugLoggingOption : INotifyPropertyChanged
    {
        private readonly Action<string, bool> _onChanged;
        private bool _isEnabled;

        public GameDebugLoggingOption(string settingsKey, string displayName, bool isEnabled, Action<string, bool> onChanged)
        {
            SettingsKey = settingsKey;
            DisplayName = displayName;
            _isEnabled = isEnabled;
            _onChanged = onChanged;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string SettingsKey { get; }

        public string DisplayName { get; }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled == value)
                {
                    return;
                }

                _isEnabled = value;
                _onChanged?.Invoke(SettingsKey, value);
                OnPropertyChanged();
            }
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class GameSettingsOption : INotifyPropertyChanged
    {
        private readonly Func<bool> _getRecordingEnabled;
        private readonly Action<bool> _setRecordingEnabled;
        private readonly Func<bool> _getDebugLoggingEnabled;
        private readonly Action<bool> _setDebugLoggingEnabled;

        public GameSettingsOption(
            string settingsKey,
            string displayName,
            Func<bool> getRecordingEnabled,
            Action<bool> setRecordingEnabled,
            Func<bool> getDebugLoggingEnabled,
            Action<bool> setDebugLoggingEnabled)
        {
            SettingsKey = settingsKey;
            DisplayName = displayName;
            _getRecordingEnabled = getRecordingEnabled;
            _setRecordingEnabled = setRecordingEnabled;
            _getDebugLoggingEnabled = getDebugLoggingEnabled;
            _setDebugLoggingEnabled = setDebugLoggingEnabled;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string SettingsKey { get; }

        public string DisplayName { get; }

        public bool IsRecordingEnabled
        {
            get => _getRecordingEnabled?.Invoke() == true;
            set
            {
                if (IsRecordingEnabled == value)
                {
                    return;
                }

                _setRecordingEnabled?.Invoke(value);
                OnPropertyChanged();
            }
        }

        public bool IsDebugLoggingEnabled
        {
            get => _getDebugLoggingEnabled?.Invoke() == true;
            set
            {
                if (IsDebugLoggingEnabled == value)
                {
                    return;
                }

                _setDebugLoggingEnabled?.Invoke(value);
                OnPropertyChanged();
            }
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
