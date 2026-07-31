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
}
