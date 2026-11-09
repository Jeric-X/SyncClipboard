using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.ViewModels;

public sealed partial class HistoryTagBarItem : ObservableObject
{
    internal HistoryTagOrderItem OrderItem { get; }
    public string Name => OrderItem.Name;
    public string Text { get; }
    private readonly Action<HistoryTagBarItem, bool> setPinned;
    private bool refreshing;

    [ObservableProperty]
    private bool isPinned;

    internal HistoryTagBarItem(HistoryTagOrderItem item, bool showCounts, Action<HistoryTagBarItem, bool> setPinned)
    {
        OrderItem = item;
        Text = showCounts ? item.Text : item.Name;
        this.setPinned = setPinned;
    }

    partial void OnIsPinnedChanged(bool value)
    {
        if (!refreshing)
            setPinned(this, value);
    }

    internal void RefreshPinned(bool value)
    {
        refreshing = true;
        IsPinned = value;
        refreshing = false;
    }
}

// A dialog-local draft: only the caller's Apply action persists PinnedNames.
public sealed partial class HistoryTagBarSettingsViewModel : ObservableObject
{
    private readonly HistoryTagOrderViewModel order;
    private readonly Dictionary<string, HistoryTagBarItem> items;
    private string[] savedPinnedNames;
    public ObservableCollection<HistoryTagBarItem> Tags { get; } = [];
    public ObservableCollection<HistoryTagBarItem> PinnedTags { get; } = [];
    public ObservableCollection<HistoryTagBarItem> UnpinnedTags { get; } = [];
    public string[] PinnedNames => order.PinnedNames;
    public int PinnedCount => order.Pinned.Count;
    public bool IsEmpty => Tags.Count == 0;
    public bool HasChanges => !savedPinnedNames.SequenceEqual(PinnedNames, StringComparer.Ordinal);

    public void AcceptChanges() => savedPinnedNames = PinnedNames;

    [ObservableProperty]
    private string error = string.Empty;

    public HistoryTagBarSettingsViewModel(IReadOnlyDictionary<string, int> counts,
        IEnumerable<string> pinned, bool showCounts = false)
    {
        order = new(counts, pinned);
        savedPinnedNames = order.PinnedNames;
        items = order.Pinned.Concat(order.Unpinned).ToDictionary(item => item.Name,
            item => new HistoryTagBarItem(item, showCounts, SetPinned), StringComparer.Ordinal);
        Refresh();
    }

    private void SetPinned(HistoryTagBarItem item, bool pinned) => Move(item, pinned, PinnedCount);

    // The insertion boundary is measured before removing a source from the pinned group.
    public void Move(HistoryTagBarItem item, bool pinned, int boundary)
    {
        if (!Tags.Contains(item))
            return;
        order.Move(item.OrderItem, pinned, Math.Clamp(boundary, 0, PinnedCount));
        Refresh();
    }

    private void Refresh()
    {
        var preview = order.Pinned.Concat(order.Unpinned).Select(item => items[item.Name]).ToArray();
        for (var index = 0; index < preview.Length; index++)
        {
            var item = preview[index];
            item.RefreshPinned(index < order.Pinned.Count);
            var oldIndex = Tags.IndexOf(item);
            if (oldIndex < 0)
                Tags.Insert(index, item);
            else if (oldIndex != index)
                Tags.Move(oldIndex, index);
        }
        while (Tags.Count > preview.Length)
            Tags.RemoveAt(Tags.Count - 1);
        UpdateGroup(PinnedTags, preview.Where(item => item.IsPinned));
        UpdateGroup(UnpinnedTags, preview.Where(item => !item.IsPinned));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private static void UpdateGroup(ObservableCollection<HistoryTagBarItem> group, IEnumerable<HistoryTagBarItem> items)
    {
        var index = 0;
        foreach (var item in items)
        {
            var oldIndex = group.IndexOf(item);
            if (oldIndex < 0)
                group.Insert(index, item);
            else if (oldIndex != index)
                group.Move(oldIndex, index);
            index++;
        }
        while (group.Count > index)
            group.RemoveAt(group.Count - 1);
    }
}

// A drag preview changes presentation only. Releasing outside either group discards it.
public sealed class HistoryTagBarDragPreview(HistoryTagBarSettingsViewModel model, HistoryTagBarItem item)
{
    private readonly HistoryTagBarSettingsViewModel model = model;
    public HistoryTagBarItem Item { get; } = item;
    public bool StartedPinned { get; } = item.IsPinned;
    public ObservableCollection<HistoryTagBarItem> PinnedTags { get; } = new(model.PinnedTags.Where(tag => tag != item));
    public ObservableCollection<HistoryTagBarItem> UnpinnedTags { get; } = new(model.UnpinnedTags);
    public bool? TargetPinned { get; private set; }

    public void PreviewPinned(int index)
    {
        RemoveUnpinnedPlaceholder();
        var oldIndex = PinnedTags.IndexOf(Item);
        var maxIndex = PinnedTags.Count - (oldIndex >= 0 ? 1 : 0);
        index = Math.Clamp(index, 0, maxIndex);
        if (oldIndex < 0)
            PinnedTags.Insert(index, Item);
        else if (oldIndex != index)
            PinnedTags.Move(oldIndex, index);
        TargetPinned = true;
    }

    public void PreviewUnpinned()
    {
        PinnedTags.Remove(Item);
        if (StartedPinned && !UnpinnedTags.Contains(Item))
        {
            var index = UnpinnedTags.TakeWhile(tag => StringComparer.Ordinal.Compare(tag.Name, Item.Name) < 0).Count();
            UnpinnedTags.Insert(index, Item);
        }
        TargetPinned = false;
    }

    public void ClearTarget()
    {
        PinnedTags.Remove(Item);
        RemoveUnpinnedPlaceholder();
        TargetPinned = null;
    }

    private void RemoveUnpinnedPlaceholder()
    {
        if (StartedPinned)
            UnpinnedTags.Remove(Item);
    }

    public void Commit()
    {
        if (TargetPinned is not { } pinned)
            return;
        var index = PinnedTags.IndexOf(Item);
        var originalIndex = model.PinnedTags.IndexOf(Item);
        if (StartedPinned && index > originalIndex)
            index++;
        model.Move(Item, pinned, Math.Max(0, index));
    }
}
