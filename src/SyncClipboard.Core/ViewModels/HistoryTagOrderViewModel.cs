using System.Collections.ObjectModel;

namespace SyncClipboard.Core.ViewModels;

public sealed record HistoryTagOrderItem(string Name, int Count)
{
    public string Text => $"{Name} ({Count})";
}

public sealed class HistoryTagOrderViewModel
{
    public ObservableCollection<HistoryTagOrderItem> Pinned { get; }
    public ObservableCollection<HistoryTagOrderItem> Unpinned { get; }
    public string[] PinnedNames => Pinned.Select(item => item.Name).ToArray();

    public HistoryTagOrderViewModel(IReadOnlyDictionary<string, int> counts, IEnumerable<string> pinned)
    {
        var names = pinned.Distinct(StringComparer.Ordinal).ToArray();
        Pinned = new(names.Select(name => new HistoryTagOrderItem(name, counts.GetValueOrDefault(name))));
        Unpinned = new(counts.Keys.Except(names, StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(name => new HistoryTagOrderItem(name, counts[name])));
    }

    // Index is the insertion boundary before removing the source item.
    public void Move(HistoryTagOrderItem item, bool pinned, int index)
    {
        var source = Pinned.Contains(item) ? Pinned : Unpinned;
        var oldIndex = source.IndexOf(item);
        if (oldIndex < 0 || (!pinned && ReferenceEquals(source, Unpinned)))
            return;
        if (pinned && ReferenceEquals(source, Pinned) && oldIndex < index)
            index--;
        source.RemoveAt(oldIndex);
        if (pinned)
        {
            Pinned.Insert(Math.Clamp(index, 0, Pinned.Count), item);
        }
        else
        {
            var sortedIndex = Unpinned.TakeWhile(tag => StringComparer.Ordinal.Compare(tag.Name, item.Name) < 0).Count();
            Unpinned.Insert(sortedIndex, item);
        }
    }
}
