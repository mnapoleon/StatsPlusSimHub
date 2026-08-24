using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace StatsPlus.Tests
{
    [TestClass]
    public class GameHistoryTabSearchTests
    {
        [TestMethod]
        public void SearchText_PartialTermFiltersImmediatelyAcrossAllFields()
        {
            StoredTrackSummary nordschleife = Summary("Porsche 911", "Nurburgring", "Nordschleife");
            StoredTrackSummary monza = Summary("Porsche 911", "Monza", "Grand Prix");
            GameHistoryTab tab = Tab(nordschleife, monza);

            tab.SearchText = "Nor";
            CollectionAssert.AreEqual(new[] { nordschleife }, tab.FilteredTracks.ToArray());

            tab.SearchText = "Nord";
            CollectionAssert.AreEqual(new[] { nordschleife }, tab.FilteredTracks.ToArray());
        }

        [TestMethod]
        public void SearchText_MultipleTermsCanMatchDifferentFieldsInAllFieldsMode()
        {
            StoredTrackSummary matching = Summary("BMW M4 GT3", "Nurburgring", "Nordschleife");
            StoredTrackSummary wrongCar = Summary("Porsche 911", "Nurburgring", "Nordschleife");
            GameHistoryTab tab = Tab(matching, wrongCar);

            tab.SearchText = "bmw nord";

            CollectionAssert.AreEqual(new[] { matching }, tab.FilteredTracks.ToArray());
        }

        [TestMethod]
        public void SelectedSearchField_RestrictsMatchingToThatDisplayField()
        {
            StoredTrackSummary summary = Summary("Nord Car", "Nurburgring", "Grand Prix");
            GameHistoryTab tab = Tab(summary);
            tab.SearchText = "Nord";

            tab.SelectedSearchField = HistorySearchField.Circuit;
            Assert.AreEqual(0, tab.FilteredTracks.Count);
            Assert.IsTrue(tab.HasNoMatchingHistory);

            tab.SelectedSearchField = HistorySearchField.Car;
            CollectionAssert.AreEqual(new[] { summary }, tab.FilteredTracks.ToArray());
        }

        [TestMethod]
        public void Search_UsesDisplayedValuesAndHandlesNullsCaseInsensitively()
        {
            StoredTrackSummary displayed = Summary(null, "Suzuka Circuit", null);
            GameHistoryTab tab = Tab(displayed);

            tab.SearchText = "ZuKa";

            CollectionAssert.AreEqual(new[] { displayed }, tab.FilteredTracks.ToArray());
        }

        [TestMethod]
        public void BlankSearchAndClear_RestoreAllRowsInSourceOrderAndDefaultField()
        {
            StoredTrackSummary first = Summary("BMW", "Spa", "GP");
            StoredTrackSummary second = Summary("Audi", "Monza", "GP");
            GameHistoryTab tab = Tab(first, second);
            tab.SelectedSearchField = HistorySearchField.Car;
            tab.SearchText = "Audi";

            tab.ClearSearch();

            Assert.AreEqual(string.Empty, tab.SearchText);
            Assert.AreEqual(HistorySearchField.AllFields, tab.SelectedSearchField);
            CollectionAssert.AreEqual(new[] { first, second }, tab.FilteredTracks.ToArray());

            tab.SearchText = "   ";
            CollectionAssert.AreEqual(new[] { first, second }, tab.FilteredTracks.ToArray());
            Assert.IsFalse(tab.HasNoMatchingHistory);
        }

        [TestMethod]
        public void RestoreSearchState_AppliesBothValuesAndRaisesOneFilterChange()
        {
            GameHistoryTab tab = Tab(Summary("BMW", "Spa", "GP"));
            int filterChanges = 0;
            tab.FilterChanged += (sender, args) => filterChanges++;

            tab.RestoreSearchState("spa", HistorySearchField.Circuit);

            Assert.AreEqual("spa", tab.SearchText);
            Assert.AreEqual(HistorySearchField.Circuit, tab.SelectedSearchField);
            Assert.AreEqual(1, filterChanges);
        }

        private static GameHistoryTab Tab(params StoredTrackSummary[] summaries)
        {
            return new GameHistoryTab
            {
                GameName = "Test Game",
                Tracks = new List<StoredTrackSummary>(summaries)
            };
        }

        private static StoredTrackSummary Summary(string car, string circuit, string layout)
        {
            return new StoredTrackSummary
            {
                CarModelDisplay = car,
                CircuitNameDisplay = circuit,
                CircuitLayoutDisplay = layout
            };
        }
    }
}
