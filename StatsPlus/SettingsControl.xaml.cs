using System.Windows;
using System.Windows.Controls;

namespace StatsPlus
{
    public partial class SettingsControl : UserControl
    {
        private readonly StatsPlusPlugin _plugin;

        public SettingsControl(StatsPlusPlugin plugin)
        {
            _plugin = plugin;
            InitializeComponent();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            _plugin.SaveSettings();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _plugin.ResetSettings();
        }

        private void RefreshHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            _plugin.RefreshStoredTrackSummaries();
        }

        private void ClearHistorySearchButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is GameHistoryTab gameTab)
            {
                gameTab.ClearSearch();
            }
        }

        private void LapValidityCheckBox_Click(object sender, RoutedEventArgs e)
        {
            CheckBox checkBox = sender as CheckBox;
            if (checkBox?.DataContext is RecordedLapView lap && checkBox.IsChecked.HasValue)
            {
                _plugin.SetLapValidity(lap, checkBox.IsChecked.Value);
            }
        }

        private void ClearGameDataButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.CommandParameter is string gameName)
            {
                _plugin.ClearGameData(gameName);
            }
        }

        private void ClearAllDataButton_Click(object sender, RoutedEventArgs e)
        {
            _plugin.ClearAllData();
        }
    }
}
