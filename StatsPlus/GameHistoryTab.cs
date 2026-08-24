using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace StatsPlus
{
    public enum HistorySearchField
    {
        AllFields,
        Car,
        Circuit,
        Layout
    }

    public sealed class HistorySearchFieldOption
    {
        public HistorySearchFieldOption(HistorySearchField field, string label)
        {
            Field = field;
            Label = label;
        }

        public HistorySearchField Field { get; }
        public string Label { get; }
    }

    internal static class HistorySummaryMatcher
    {
        internal static bool Matches(StoredTrackSummary summary, string searchText, HistorySearchField field)
        {
            if (summary == null)
            {
                return false;
            }

            string[] terms = (searchText ?? string.Empty)
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0)
            {
                return true;
            }

            string[] values = SearchableValues(summary, field);
            return terms.All(term => values.Any(value =>
                (value ?? string.Empty).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static string[] SearchableValues(StoredTrackSummary summary, HistorySearchField field)
        {
            switch (field)
            {
                case HistorySearchField.Car:
                    return new[] { summary.CarModelDisplay };
                case HistorySearchField.Circuit:
                    return new[] { summary.CircuitNameDisplay };
                case HistorySearchField.Layout:
                    return new[] { summary.CircuitLayoutDisplay };
                default:
                    return new[]
                    {
                        summary.CarModelDisplay,
                        summary.CircuitNameDisplay,
                        summary.CircuitLayoutDisplay
                    };
            }
        }
    }

    public class GameHistoryTab : INotifyPropertyChanged
    {
        private static readonly IReadOnlyList<HistorySearchFieldOption> Fields =
            Array.AsReadOnly(new[]
            {
                new HistorySearchFieldOption(HistorySearchField.AllFields, "All fields"),
                new HistorySearchFieldOption(HistorySearchField.Car, "Car"),
                new HistorySearchFieldOption(HistorySearchField.Circuit, "Circuit"),
                new HistorySearchFieldOption(HistorySearchField.Layout, "Layout")
            });

        private List<StoredTrackSummary> _tracks = new List<StoredTrackSummary>();
        private string _searchText = string.Empty;
        private HistorySearchField _selectedSearchField = HistorySearchField.AllFields;

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler FilterChanged;

        public string Header => GameName;
        public string GameName { get; set; } = string.Empty;

        public List<StoredTrackSummary> Tracks
        {
            get => _tracks;
            set
            {
                _tracks = value ?? new List<StoredTrackSummary>();
                OnPropertyChanged();
                ApplyFilter();
            }
        }

        public ObservableCollection<StoredTrackSummary> FilteredTracks { get; } =
            new ObservableCollection<StoredTrackSummary>();

        public IReadOnlyList<HistorySearchFieldOption> SearchFieldOptions => Fields;

        public string SearchText
        {
            get => _searchText;
            set
            {
                string next = value ?? string.Empty;
                if (string.Equals(_searchText, next, StringComparison.Ordinal))
                {
                    return;
                }

                _searchText = next;
                OnPropertyChanged();
                ApplyFilter();
            }
        }

        public HistorySearchField SelectedSearchField
        {
            get => _selectedSearchField;
            set
            {
                if (_selectedSearchField == value)
                {
                    return;
                }

                _selectedSearchField = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }

        public bool HasNoMatchingHistory =>
            _tracks.Count > 0 &&
            !string.IsNullOrWhiteSpace(_searchText) &&
            FilteredTracks.Count == 0;

        public bool ContainsVisible(StoredTrackSummary summary)
        {
            return summary != null && FilteredTracks.Contains(summary);
        }

        public void RestoreSearchState(string searchText, HistorySearchField field)
        {
            _searchText = searchText ?? string.Empty;
            _selectedSearchField = field;
            OnPropertyChanged(nameof(SearchText));
            OnPropertyChanged(nameof(SelectedSearchField));
            ApplyFilter();
        }

        public void ClearSearch()
        {
            RestoreSearchState(string.Empty, HistorySearchField.AllFields);
        }

        public override string ToString()
        {
            return GameName;
        }

        private void ApplyFilter()
        {
            List<StoredTrackSummary> matches = _tracks
                .Where(summary => HistorySummaryMatcher.Matches(summary, _searchText, _selectedSearchField))
                .ToList();

            FilteredTracks.Clear();
            foreach (StoredTrackSummary summary in matches)
            {
                FilteredTracks.Add(summary);
            }

            OnPropertyChanged(nameof(HasNoMatchingHistory));
            FilterChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
