using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StatsPlus
{
    public class PluginSettings : INotifyPropertyChanged
    {
        private bool _enablePlugin = true;
        private bool _publishTrackInfo = true;
        private string _customLabel = "StatsPlus";
        private bool _recordAssettoCorsa = true;
        private bool _recordAssettoCorsaEvo = true;
        private bool _recordAutomobilista2 = true;
        private bool _recordIRacing = true;
        private bool _recordRFactor2 = true;
        private bool _recordR3E = true;

        public event PropertyChangedEventHandler PropertyChanged;

        public bool EnablePlugin
        {
            get => _enablePlugin;
            set
            {
                if (_enablePlugin == value)
                {
                    return;
                }

                _enablePlugin = value;
                OnPropertyChanged();
            }
        }

        public bool PublishTrackInfo
        {
            get => _publishTrackInfo;
            set
            {
                if (_publishTrackInfo == value)
                {
                    return;
                }

                _publishTrackInfo = value;
                OnPropertyChanged();
            }
        }

        public string CustomLabel
        {
            get => _customLabel;
            set
            {
                var nextValue = string.IsNullOrWhiteSpace(value) ? "StatsPlus" : value.Trim();
                if (_customLabel == nextValue)
                {
                    return;
                }

                _customLabel = nextValue;
                OnPropertyChanged();
            }
        }

        public bool RecordAssettoCorsa
        {
            get => _recordAssettoCorsa;
            set
            {
                if (_recordAssettoCorsa == value)
                {
                    return;
                }

                _recordAssettoCorsa = value;
                OnPropertyChanged();
            }
        }

        public bool RecordAssettoCorsaEvo
        {
            get => _recordAssettoCorsaEvo;
            set
            {
                if (_recordAssettoCorsaEvo == value)
                {
                    return;
                }

                _recordAssettoCorsaEvo = value;
                OnPropertyChanged();
            }
        }

        public bool RecordAutomobilista2
        {
            get => _recordAutomobilista2;
            set
            {
                if (_recordAutomobilista2 == value)
                {
                    return;
                }

                _recordAutomobilista2 = value;
                OnPropertyChanged();
            }
        }

        public bool RecordIRacing
        {
            get => _recordIRacing;
            set
            {
                if (_recordIRacing == value)
                {
                    return;
                }

                _recordIRacing = value;
                OnPropertyChanged();
            }
        }

        public bool RecordRFactor2
        {
            get => _recordRFactor2;
            set
            {
                if (_recordRFactor2 == value)
                {
                    return;
                }

                _recordRFactor2 = value;
                OnPropertyChanged();
            }
        }

        public bool RecordR3E
        {
            get => _recordR3E;
            set
            {
                if (_recordR3E == value)
                {
                    return;
                }

                _recordR3E = value;
                OnPropertyChanged();
            }
        }

        public void Reset()
        {
            EnablePlugin = true;
            PublishTrackInfo = true;
            CustomLabel = "StatsPlus";
            RecordAssettoCorsa = true;
            RecordAssettoCorsaEvo = true;
            RecordAutomobilista2 = true;
            RecordIRacing = true;
            RecordRFactor2 = true;
            RecordR3E = true;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
