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

        private void ToggleLapValidityButton_Click(object sender, RoutedEventArgs e)
        {
            _plugin.ToggleSelectedLapValidity();
        }

        private void ClearSelectedGameButton_Click(object sender, RoutedEventArgs e)
        {
            _plugin.ClearSelectedGameData();
        }

        private void ClearAllDataButton_Click(object sender, RoutedEventArgs e)
        {
            _plugin.ClearAllData();
        }
    }
}
