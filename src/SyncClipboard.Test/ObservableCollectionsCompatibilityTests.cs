using ObservableCollections;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.ViewModels;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SyncClipboard.Test;

[TestClass]
[TestCategory("NonUI")]
public class ObservableCollectionsCompatibilityTests
{
    [TestMethod]
    public void FilteredMutations_ReportVisibleIndexesAndPreserveRecordIdentity()
    {
        var hidden = Record(0);
        var first = Record(1, starred: true);
        var last = Record(2, starred: true);
        ObservableList<HistoryRecord> source = [hidden, first, last];
        using var view = source.CreateView(x => x);
        view.AttachFilter(x => x.Stared);
        using var items = view.ToNotifyCollectionChanged();
        List<string> events = [];
        items.CollectionChanged += (_, e) => events.Add(Describe(e));

        var inserted = Record(3, starred: true);
        source.Insert(2, inserted);
        AssertOrder(items, "1,3,2");
        Assert.AreSame(inserted, items[1]);
        source.RemoveAt(0); // A rejected record must still update internal source-index mappings.
        Assert.HasCount(1, events);
        source.RemoveAt(1);
        var replacement = Record(4, starred: true);
        source[0] = replacement;

        AssertOrder(items, "4,2");
        Assert.AreSame(replacement, items[0]);
        Assert.AreSame(last, items[1]);
        Assert.AreEqual("Add:1:-1:3:|Remove:-1:1::3|Replace:0:0:4:1", string.Join('|', events));
    }

    [TestMethod]
    public void FilterRefresh_PreservesSelectedKeysAndOriginalObjectsAcrossHiddenRecords()
    {
        var first = Record(1, starred: true);
        var second = Record(2);
        var third = Record(3, starred: true);
        ObservableList<HistoryRecord> source = [first, second, third];
        using var view = source.CreateView(x => x);
        using var items = view.ToNotifyCollectionChanged();
        HashSet<HistoryRecordKey> selected = [HistoryRecordKey.From(first), HistoryRecordKey.From(second)];
        var resets = 0;
        items.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) resets++; };

        view.AttachFilter(x => x.Stared);
        AssertOrder(items, "1,3");
        Assert.AreEqual("1", string.Join(',', items.Where(x => selected.Contains(HistoryRecordKey.From(x))).Select(x => x.Hash)));
        Assert.HasCount(2, selected);
        Assert.AreSame(first, items[0]);

        second.Stared = true;
        view.AttachFilter(x => x.Stared);
        AssertOrder(items, "1,2,3");
        Assert.AreSame(second, items[1]);
        Assert.AreEqual("1,2", string.Join(',', items.Where(x => selected.Contains(HistoryRecordKey.From(x))).Select(x => x.Hash)));
        view.AttachFilter(x => x.ID == 2);
        AssertOrder(items, "2");
        view.ResetFilter();
        AssertOrder(items, "1,2,3");
        AssertOrder(source, "1,2,3");
        Assert.IsGreaterThanOrEqualTo(3, resets);
        Assert.HasCount(2, selected);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PageAppends_EmitSingleItemNotificationsWithCoherentCounts(bool filtered)
    {
        ObservableList<HistoryRecord> source = [];
        using var view = source.CreateView(x => x);
        if (filtered) view.AttachFilter(x => x.Stared);
        using var items = view.ToNotifyCollectionChanged();
        var seen = 0;
        var countChanges = 0;
        items.CollectionChanged += (_, e) =>
        {
            Assert.AreEqual(NotifyCollectionChangedAction.Add, e.Action);
            Assert.AreEqual(1, e.NewItems!.Count);
            Assert.AreEqual(seen, e.NewStartingIndex);
            seen++;
            Assert.AreEqual(seen, items.Count);
            Assert.AreSame(e.NewItems![0], items[seen - 1]);
        };
        ((INotifyPropertyChanged)items).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(items.Count)) countChanges++;
        };

        var firstPage = Enumerable.Range(1, 20).Select(x => Record(x, starred: x % 2 == 0)).ToArray();
        var secondPage = Enumerable.Range(21, 20).Select(x => Record(x, starred: x % 2 == 0)).ToArray();
        source.AddRange(firstPage);
        source.AddRange(secondPage);

        Assert.AreEqual(filtered ? 20 : 40, seen);
        Assert.AreEqual(seen, countChanges);
        Assert.AreEqual(filtered ? "2,40" : "1,40", $"{items[0].Hash},{items[^1].Hash}");
        Assert.AreSame(secondPage[^1], items[^1]);
        Assert.AreEqual(40, source.Count);
    }

    [TestMethod]
    public void RangeRemoval_UsesVisibleIndexesAfterFilteredItemsAreRemoved()
    {
        ObservableList<HistoryRecord> source = [Record(1, true), Record(2), Record(3, true), Record(4), Record(5, true)];
        using var view = source.CreateView(x => x);
        view.AttachFilter(x => x.Stared);
        using var items = view.ToNotifyCollectionChanged();
        List<string> events = [];
        items.CollectionChanged += (_, e) => events.Add(Describe(e));

        source.RemoveRange(1, 3);

        AssertOrder(source, "1,5");
        AssertOrder(items, "1,5");
        Assert.AreEqual("Remove:-1:1::3", string.Join('|', events));
        source.RemoveAt(1);
        AssertOrder(items, "1");
        Assert.AreEqual("Remove:-1:1::5", events[^1]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SortAndReverse_PreserveFilteredOrderForBothHistoryTimeFields(bool lastAccessed)
    {
        var first = Record(1, true);
        var hidden = Record(2);
        var third = Record(3, true);
        first.LastAccessed = third.Timestamp.AddMinutes(1);
        ObservableList<HistoryRecord> source = [hidden, first, third];
        using var view = source.CreateView(x => x);
        view.AttachFilter(x => x.Stared);
        using var items = view.ToNotifyCollectionChanged();
        List<NotifyCollectionChangedAction> actions = [];
        items.CollectionChanged += (_, e) => actions.Add(e.Action);

        source.Sort(Comparer<HistoryRecord>.Create((a, b) =>
            (lastAccessed ? b.LastAccessed : b.Timestamp).CompareTo(lastAccessed ? a.LastAccessed : a.Timestamp)));

        AssertOrder(items, lastAccessed ? "1,3" : "3,1");
        source.Reverse();
        AssertOrder(items, lastAccessed ? "3,1" : "1,3");
        Assert.AreSame(first, items[lastAccessed ? 1 : 0]);
        Assert.AreSame(third, items[lastAccessed ? 0 : 1]);
        Assert.HasCount(2, actions);
        Assert.IsTrue(actions.All(x => x == NotifyCollectionChangedAction.Reset));
    }

    [TestMethod]
    [DataRow(0, 0, "2")]
    [DataRow(1, 1, "3")]
    [DataRow(2, 2, "2")]
    [DataRow(1, 0, "2")]
    public void RemovalSelection_UsesActualFilteredIndexesAndRetainsTheTargetRecord(int selectedIndex, int removedIndex, string expected)
    {
        ObservableList<HistoryRecord> source = [Record(0), Record(1, true), Record(2, true), Record(3, true)];
        using var view = source.CreateView(x => x);
        view.AttachFilter(x => x.Stared);
        using var items = view.ToNotifyCollectionChanged();
        var targetIndex = HistoryViewModel.GetSelectionTargetIndexBeforeRemoval(items.Count, selectedIndex, removedIndex);
        var target = items[targetIndex];
        var removed = items[removedIndex];

        source.Remove(removed);

        Assert.AreEqual(expected, target.Hash);
        Assert.IsGreaterThanOrEqualTo(0, items.IndexOf(target));
        Assert.AreSame(target, items[items.IndexOf(target)]);
        Assert.AreEqual(-1, items.IndexOf(removed));
    }

    [TestMethod]
    public void DefaultDispatcher_DeliversOnTheMutatingThreadWithoutPostingToSynchronizationContext()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new RejectingContext());
        try
        {
            ObservableList<HistoryRecord> source = [];
            using var view = source.CreateView(x => x);
            using var items = view.ToNotifyCollectionChanged();
            var thread = Environment.CurrentManagedThreadId;
            var notified = false;
            items.CollectionChanged += (_, _) =>
            {
                Assert.AreEqual(thread, Environment.CurrentManagedThreadId);
                notified = true;
            };

            source.Add(Record(1));

            Assert.IsTrue(notified);
            AssertOrder(items, "1");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [TestMethod]
    public void CustomDispatcher_QueuesStableEventDataWithoutARealUiThread()
    {
        ObservableList<HistoryRecord> source = [];
        using var view = source.CreateView(x => x);
        var dispatcher = new QueuedDispatcher();
        using var items = view.ToNotifyCollectionChanged(dispatcher);
        List<string> events = [];
        items.CollectionChanged += (_, e) => events.Add(Describe(e));

        source.AddRange([Record(1), Record(2)]);
        source.RemoveAt(0);
        Assert.IsEmpty(events);
        AssertOrder(items, "2");
        dispatcher.Drain();

        Assert.AreEqual("Add:0:-1:1:|Add:1:-1:2:|Remove:-1:0::1", string.Join('|', events));
    }

    [TestMethod]
    public void DisposingAdapter_UnsubscribesItsEventsWhileTheParentViewRemainsUsable()
    {
        ObservableList<HistoryRecord> source = [Record(1)];
        using var view = source.CreateView(x => x);
        using var items = view.ToNotifyCollectionChanged();
        var notifications = 0;
        items.CollectionChanged += (_, _) => notifications++;
        items.Dispose();

        source.Add(Record(2));

        Assert.AreEqual(0, notifications);
        AssertOrder(view, "1,2");
        AssertOrder(items, "1");
    }

    [TestMethod]
    public void DisposingParent_UnsubscribesFromSourceMutations()
    {
        ObservableList<HistoryRecord> source = [Record(1)];
        using var view = source.CreateView(x => x);
        using var items = view.ToNotifyCollectionChanged();
        var notifications = 0;
        items.CollectionChanged += (_, _) => notifications++;
        view.Dispose();

        source.Clear();
        source.Add(Record(2));

        Assert.AreEqual(0, notifications);
        AssertOrder(items, "1");
    }

    [TestMethod]
    public void ClearAndReload_KeepGenericAndNonGenericListViewsConsistent()
    {
        ObservableList<HistoryRecord> source = [Record(1), Record(2)];
        using var view = source.CreateView(x => x);
        using var items = view.ToNotifyCollectionChanged();
        var nonGeneric = (IList)items;
        var resets = 0;
        items.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) resets++; };

        source.Clear();
        Assert.AreEqual(0, nonGeneric.Count);
        Assert.IsEmpty((IList<HistoryRecord>)items);
        var next = Record(3);
        source.Add(next);

        Assert.AreEqual(1, resets);
        Assert.AreEqual(1, ((ICollection)items).Count);
        Assert.AreSame(next, nonGeneric[0]);
        Assert.AreSame(next, ((IList<HistoryRecord>)items)[0]);
        Assert.AreEqual(0, items.IndexOf(next));
    }

    private static HistoryRecord Record(int id, bool starred = false) => new()
    {
        ID = id,
        Hash = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Text = $"record {id}",
        Stared = starred,
        Timestamp = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc).AddMinutes(id),
        LastAccessed = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc).AddMinutes(id)
    };

    private static void AssertOrder(IEnumerable<HistoryRecord> records, string expected) =>
        Assert.AreEqual(expected, string.Join(',', records.Select(x => x.Hash)));

    private static string Describe(NotifyCollectionChangedEventArgs e) =>
        $"{e.Action}:{e.NewStartingIndex}:{e.OldStartingIndex}:{Keys(e.NewItems)}:{Keys(e.OldItems)}";

    private static string Keys(IList? records) => records is null ? string.Empty :
        string.Join(',', records.Cast<HistoryRecord>().Select(x => x.Hash));

    private sealed class RejectingContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => throw new AssertFailedException("Unexpected context dispatch.");
        public override void Send(SendOrPostCallback d, object? state) => throw new AssertFailedException("Unexpected context dispatch.");
    }

    private sealed class QueuedDispatcher : ICollectionEventDispatcher
    {
        private readonly Queue<CollectionEventDispatcherEventArgs> _events = new();
        public void Post(CollectionEventDispatcherEventArgs ev) => _events.Enqueue(ev);
        public void Drain()
        {
            while (_events.TryDequeue(out var ev)) ev.Invoke();
        }
    }
}
