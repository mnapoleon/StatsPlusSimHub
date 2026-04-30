using System.Windows;
using System.Windows.Controls;

namespace Affinity
{
    public partial class SettingsControl : UserControl
    {
        private readonly AffinityPlugin _plugin;

        public SettingsControl(AffinityPlugin plugin)
        {
            _plugin = plugin;
            InitializeComponent();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            _plugin.SaveSettings();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _plugin.RefreshDistanceSummaries();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _plugin.ResetSettings();
        }

        private void DistanceUnitChanged(object sender, RoutedEventArgs e)
        {
            _plugin.RefreshDisplaySettings();
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            _plugin.ClearAllData();
        }
    }
}
