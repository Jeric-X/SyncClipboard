using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.Runner;
using SyncClipboard.Core.ViewModels.Sub;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace SyncClipboard.Core.ViewModels;

/// <summary>History-list selection state, navigation, gestures, and batch operations.</summary>
public partial class HistoryViewModel
{
    [Flags]
    private enum SelectionSummaryPart
    {
        None = 0,
        Counts = 1,
        All = Counts,
    }

    private readonly record struct FilteredHistoryRecord(HistoryRecordKey Key, bool Starred);

    private sealed class SelectedHistoryRecordInfo(bool starred)
    {
        public bool Starred { get; set; } = starred;
    }

    private HistoryRecordVM? selectedItem;

    /// <summary>The current preview/navigation record, independent of the batch selection.</summary>
    public HistoryRecordVM? SelectedItem
    {
        get => selectedItem;
        set
        {
            // Records with the same key can be different instances after an update.
            if (ReferenceEquals(selectedItem, value))
                return;

            OnPropertyChanging();
            selectedItem = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PreviewedHistoryItem));
        }
    }

    public HistoryRecordVM? PreviewedHistoryItem => SelectedItem;

    // Resolve positions only when navigating; collection changes do not need to maintain an index.
    private int SelectedIndex
    {
        get => SelectedItem is { } record ? HistoryItems.IndexOf(record) : -1;
        set => SelectedItem = value >= 0 && value < HistoryItemCount
            ? ((IList<HistoryRecordVM>)HistoryItems)[value]
            : null;
    }

    private int HistoryItemCount => ((ICollection)HistoryItems).Count;

    /// <summary>Global selection keyed by identity, including per-record selection metadata.</summary>
    private readonly Dictionary<HistoryRecordKey, SelectedHistoryRecordInfo> selectedHistoryRecords = [];
    private HistoryRecordKey? selectionAnchor;
    private readonly ObservableCollection<HistoryRecordVM> visibleSelectedItems = [];
    private CoalescingTask<SelectionSummaryPart> selectionSummaryRefreshTask = null!;
    private readonly Lock multiSelectLongPressLock = new();
    private CancellationTokenSource? multiSelectLongPressCancellation;
    private static readonly TimeSpan MultiSelectLongPressDelay = TimeSpan.FromMilliseconds(600);

    public ReadOnlyObservableCollection<HistoryRecordVM> VisibleSelectedItems { get; private set; } = null!;

    [ObservableProperty]
    private bool isMultiSelecting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedRecords))]
    [NotifyCanExecuteChangedFor(nameof(ExportSelectedCommand), nameof(ConfirmDeleteSelectedCommand), nameof(ConfirmToggleSelectedStarredCommand))]
    private int selectedHistoryCount;

    public bool HasSelectedRecords => SelectedHistoryCount > 0;

    /// <summary>The number of selected records that are currently starred.</summary>
    [ObservableProperty]
    private int selectedStarredHistoryCount;

    [ObservableProperty]
    private int selectedInCurrentFilterCount;

    /// <summary>True, false or null for checked, unchecked and partial current-filter selection.</summary>
    [ObservableProperty]
    private bool? isCurrentFilterFullySelected = false;

    [ObservableProperty]
    private bool areSelectedRecordsStarred;

    private void InitializeSelection()
    {
        VisibleSelectedItems = new ReadOnlyObservableCollection<HistoryRecordVM>(visibleSelectedItems);
        selectionSummaryRefreshTask = new(
            (pending, next) => pending | next,
            RefreshSelectionSummaryAsync,
            ex => logger.WriteAsync("Failed to refresh history selection summary:", ex.Message));
    }

    private async void OnHistoryItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            ClearVisibleSelectedItems();
            if (IsMultiSelecting)
                RequestSelectionSummaryRefresh(SelectionSummaryPart.Counts);
            ClearSelectedItem();
            return;
        }

        foreach (var record in e.OldItems?.OfType<HistoryRecordVM>() ?? [])
            SetVisibleRecordSelection(record, false);

        foreach (var record in e.NewItems?.OfType<HistoryRecordVM>() ?? [])
            SetVisibleRecordSelection(record, selectedHistoryRecords.ContainsKey(record.Key));

        if (IsMultiSelecting)
        {
            RequestSelectionSummaryRefresh(SelectionSummaryPart.Counts);
            return;
        }

        if (e.Action != NotifyCollectionChangedAction.Add
            || SelectedItem is not null
            || e.NewItems?.Count <= 0)
            return;

        await Task.Delay(1);
        if (!IsMultiSelecting
            && SelectedItem is null
            && HistoryItemCount > 0)
        {
            SelectedIndex = 0;
        }
    }

    public void HandleRecordClick(HistoryRecordVM record, bool ctrlPressed, bool shiftPressed)
    {
        if (IsMultiSelecting)
        {
            HandleMultiSelectRecordClick(record, shiftPressed);
            return;
        }

        if (!ctrlPressed && !shiftPressed)
            return;

        EnterMultiSelectFromRecordClick(record, shiftPressed);
    }

    private void HandleMultiSelectRecordClick(HistoryRecordVM record, bool shiftPressed)
    {
        if (shiftPressed && selectionAnchor is { } anchor)
            SelectRange(anchor, record);
        else
            ToggleRecordSelection(record);
    }

    private void EnterMultiSelectFromRecordClick(HistoryRecordVM record, bool shiftPressed)
    {
        var current = SelectedItem;

        IsMultiSelecting = true;
        if (current is not null)
        {
            AddSelectedHistoryKey(current.Key, current.Stared);
            SetVisibleRecordSelection(current, true);
            selectionAnchor = current.Key;
        }

        if (shiftPressed && selectionAnchor is { } anchor)
            SelectRange(anchor, record);
        else if (current?.Key != record.Key)
            ToggleRecordSelection(record);
        else
            RequestSelectionSummaryRefresh(SelectionSummaryPart.Counts);
    }

    /// <summary>Sets the preview and keyboard focus to one visible history record.</summary>
    public bool SelectSingleRecord(HistoryRecordVM record)
    {
        var index = HistoryItems.IndexOf(record);
        if (index < 0)
            return false;

        SelectedItem = ((IList<HistoryRecordVM>)HistoryItems)[index];
        return true;
    }

    internal static int GetSelectionTargetIndexBeforeRemoval(
        int itemCount,
        int selectedIndex,
        int removedIndex)
    {
        if (selectedIndex < 0 || selectedIndex >= itemCount)
            return -1;

        if (selectedIndex != removedIndex)
            return selectedIndex;

        if (removedIndex + 1 < itemCount)
            return removedIndex + 1;

        return removedIndex - 1;
    }

    private HistoryRecordVM? PrepareSelectionForRecordRemoval(HistoryRecordVM removedRecord)
    {
        if (!ReferenceEquals(SelectedItem, removedRecord))
            return null;

        var items = (IList<HistoryRecordVM>)HistoryItems;
        var removedIndex = items.IndexOf(removedRecord);
        var targetIndex = IsMultiSelecting ? -1 : GetSelectionTargetIndexBeforeRemoval(
            items.Count, removedIndex, removedIndex);
        var target = targetIndex >= 0 ? items[targetIndex] : null;

        // Reset the UI selection before the collection emits remove/add events.
        // The adjacent record is selected after removal, independently of index changes.
        ClearSelectedItem();
        return target;
    }

    [RelayCommand]
    public async Task DeleteItem(HistoryRecordVM record)
    {
        await historyManager.DeleteHistory(record.ToHistoryRecord());
    }

    public void EnterMultiSelect(HistoryRecordVM record)
    {
        if (!IsMultiSelecting)
        {
            ClearSelectedHistoryKeys();
            ClearVisibleSelectedItems();
            IsMultiSelecting = true;
        }
        AddSelection(record);
    }

    /// <summary>Starts the platform-independent long-press gesture that enters multi-select mode.</summary>
    public void BeginMultiSelectLongPress(HistoryRecordVM record)
    {
        if (IsMultiSelecting)
            return;

        var cancellation = new CancellationTokenSource();
        CancellationTokenSource? previousCancellation;
        lock (multiSelectLongPressLock)
        {
            previousCancellation = multiSelectLongPressCancellation;
            multiSelectLongPressCancellation = cancellation;
        }

        CancelAndDispose(previousCancellation);
        _ = EnterMultiSelectAfterLongPressAsync(record, cancellation);
    }

    /// <summary>Cancels the pending multi-select long-press gesture, if any.</summary>
    public void CancelMultiSelectLongPress()
    {
        CancellationTokenSource? cancellation;
        lock (multiSelectLongPressLock)
        {
            cancellation = multiSelectLongPressCancellation;
            multiSelectLongPressCancellation = null;
        }

        CancelAndDispose(cancellation);
    }

    private async Task EnterMultiSelectAfterLongPressAsync(HistoryRecordVM record, CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(MultiSelectLongPressDelay, cancellation.Token);
            await _threadDispatcher.RunOnMainThreadAsync(() =>
            {
                if (!cancellation.IsCancellationRequested
                    && IsCurrentLongPress(cancellation)
                    && !IsMultiSelecting)
                    EnterMultiSelect(record);
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            ClearLongPressIfCurrent(cancellation);
        }
    }

    private bool IsCurrentLongPress(CancellationTokenSource cancellation)
    {
        lock (multiSelectLongPressLock)
            return ReferenceEquals(multiSelectLongPressCancellation, cancellation);
    }

    private void ClearLongPressIfCurrent(CancellationTokenSource cancellation)
    {
        lock (multiSelectLongPressLock)
        {
            if (!ReferenceEquals(multiSelectLongPressCancellation, cancellation))
                return;

            multiSelectLongPressCancellation = null;
        }

        cancellation.Dispose();
    }

    private static void CancelAndDispose(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
            return;

        cancellation.Cancel();
        cancellation.Dispose();
    }

    public void ToggleRecordSelection(HistoryRecordVM record)
    {
        if (!RemoveSelectedHistoryKey(record.Key))
            AddSelectedHistoryKey(record.Key, record.Stared);
        selectionAnchor = record.Key;
        SetVisibleRecordSelection(record, selectedHistoryRecords.ContainsKey(record.Key));
        RequestSelectionSummaryRefresh(SelectionSummaryPart.Counts);
    }

    private void AddSelection(HistoryRecordVM record)
    {
        AddSelectedHistoryKey(record.Key, record.Stared);
        selectionAnchor = record.Key;
        SetVisibleRecordSelection(record, true);
        RequestSelectionSummaryRefresh(SelectionSummaryPart.Counts);
    }

    private void SelectRange(HistoryRecordKey anchor, HistoryRecordVM target)
    {
        var items = (IList<HistoryRecordVM>)HistoryItems;
        var start = -1;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Key == anchor)
            {
                start = i;
                break;
            }
        }

        var end = items.IndexOf(target);
        if (start < 0 || end < 0)
        {
            ToggleRecordSelection(target);
            return;
        }

        var first = Math.Min(start, end);
        var last = Math.Max(start, end);
        for (var i = first; i <= last; i++)
        {
            var record = items[i];
            AddSelectedHistoryKey(record.Key, record.Stared);
        }
        selectionAnchor = anchor;
        RefreshVisibleSelectedItems();
        RequestSelectionSummaryRefresh();
    }

    public async Task SetCurrentFilterSelectionAsync(bool select)
    {
        var records = await GetCurrentFilterRecordsAsync();
        if (select)
        {
            foreach (var record in records)
                AddSelectedHistoryKey(record.Key, record.Starred);
        }
        else
        {
            foreach (var record in records)
                RemoveSelectedHistoryKey(record.Key);
        }
        RefreshVisibleSelectedItems();
        RequestSelectionSummaryRefresh();
    }

    [RelayCommand]
    public void ExitMultiSelect()
    {
        ClearSelectedHistoryKeys();
        selectionAnchor = null;
        ClearVisibleSelectedItems();
        IsMultiSelecting = false;
        RequestSelectionSummaryRefresh(SelectionSummaryPart.Counts);
    }

    [RelayCommand]
    private async Task ToggleCurrentFilterSelectionAsync() =>
        await SetCurrentFilterSelectionAsync(IsCurrentFilterFullySelected != true);

    [RelayCommand(CanExecute = nameof(HasSelectedRecords))]
    private async Task ConfirmDeleteSelectedAsync()
    {
        if (!HasSelectedRecords)
            return;

        var dialog = _serviceProvider.GetRequiredKeyedService<IMainWindowDialog>("HistoryWindow");
        if (await dialog.ShowConfirmationAsync(
                Strings.HistoryMultiSelectDelete,
                string.Format(Strings.HistoryMultiSelectDeleteConfirmMessage, SelectedHistoryCount)))
            await RunWithOperationTimeoutAsync("delete selected history records", DeleteSelectedAsync);
    }

    private async Task DeleteSelectedAsync(CancellationToken token)
    {
        var keys = selectedHistoryRecords.Keys.ToArray();
        foreach (var key in keys)
        {
            var record = await historyManager.GetHistoryRecord(key.Hash, key.Type, token);
            if (record is not null)
                await historyManager.DeleteHistory(record, token);
        }
        ClearSelectedHistoryKeys();
        ClearVisibleSelectedItems();
        RequestSelectionSummaryRefresh(SelectionSummaryPart.Counts);
    }

    private async Task SetSelectedStarredAsync(bool starred)
    {
        var keys = selectedHistoryRecords.Keys.ToArray();
        await historyManager.SetStarredAsync(keys, starred);
        SetSelectedRecordsStarredState(keys, starred);
        if (IsStarredScopeActive)
            RequestSelectionSummaryRefresh(SelectionSummaryPart.Counts);
    }

    private Task ToggleSelectedStarredAsync() => SetSelectedStarredAsync(!AreSelectedRecordsStarred);

    [RelayCommand(CanExecute = nameof(HasSelectedRecords))]
    private async Task ConfirmToggleSelectedStarredAsync()
    {
        if (!HasSelectedRecords)
            return;

        var isUnstar = AreSelectedRecordsStarred;
        var dialog = _serviceProvider.GetRequiredKeyedService<IMainWindowDialog>("HistoryWindow");
        var title = isUnstar ? Strings.HistoryMultiSelectUnstar : Strings.HistoryMultiSelectStar;
        var message = isUnstar
            ? string.Format(Strings.HistoryMultiSelectUnstarConfirmMessage, SelectedHistoryCount)
            : string.Format(Strings.HistoryMultiSelectStarConfirmMessage, SelectedHistoryCount);
        if (await dialog.ShowConfirmationAsync(title, message))
            await ToggleSelectedStarredAsync();
    }

    private async Task<List<FilteredHistoryRecord>> GetCurrentFilterRecordsAsync(CancellationToken token = default)
    {
        if (SelectedFilter == HistoryFilterType.Transferring)
            return allHistoryItems.Select(record => new FilteredHistoryRecord(record.Key, record.Stared)).ToList();
        var (types, starred, searchText) = BuildQueryParameters();
        var records = await historyManager.GetHistoryAsync(
            types, starred, null, int.MaxValue, searchText, SortByLastAccessed, token);
        return records.Select(record => new FilteredHistoryRecord(HistoryRecordKey.From(record), record.Stared)).ToList();
    }

    private async Task RefreshSelectionSummaryAsync(SelectionSummaryPart parts, CancellationToken token)
    {
        if (parts.HasFlag(SelectionSummaryPart.Counts))
        {
            var selectedKeys = selectedHistoryRecords.Keys.ToArray();
            int currentRecordCount;
            int selectedInCurrentFilter;
            if (SelectedFilter == HistoryFilterType.Transferring)
            {
                currentRecordCount = allHistoryItems.Count;
                selectedInCurrentFilter = allHistoryItems.Count(
                    record => selectedHistoryRecords.ContainsKey(record.Key));
            }
            else
            {
                var (types, starred, searchText) = BuildQueryParameters();
                (currentRecordCount, selectedInCurrentFilter) =
                    await historyManager.GetHistorySelectionCountsAsync(
                        types,
                        starred,
                        searchText,
                        selectedKeys,
                        token);
            }

            if (!HasSameSelectedHistoryKeys(selectedKeys))
                return;

            SelectedInCurrentFilterCount = selectedInCurrentFilter;
            IsCurrentFilterFullySelected = currentRecordCount == 0 ? false :
                selectedInCurrentFilter == 0 ? false :
                selectedInCurrentFilter == currentRecordCount ? true : null;
        }
    }

    private void RequestSelectionSummaryRefresh(SelectionSummaryPart parts = SelectionSummaryPart.All)
    {
        if (parts == SelectionSummaryPart.None)
            return;

        _ = selectionSummaryRefreshTask.RunAsync(parts);
    }

    private bool AddSelectedHistoryKey(HistoryRecordKey key, bool starred)
    {
        if (!selectedHistoryRecords.TryAdd(key, new SelectedHistoryRecordInfo(starred)))
        {
            SetSelectedRecordStarredState(key, starred);
            return false;
        }

        SelectedHistoryCount = selectedHistoryRecords.Count;
        if (starred)
            SelectedStarredHistoryCount++;
        UpdateAreSelectedRecordsStarred();
        return true;
    }

    private bool RemoveSelectedHistoryKey(HistoryRecordKey key)
    {
        if (!selectedHistoryRecords.Remove(key, out var recordInfo))
            return false;

        if (recordInfo.Starred == true)
            SelectedStarredHistoryCount--;

        SelectedHistoryCount = selectedHistoryRecords.Count;
        UpdateAreSelectedRecordsStarred();
        return true;
    }

    private void ClearSelectedHistoryKeys()
    {
        selectedHistoryRecords.Clear();
        SelectedHistoryCount = 0;
        SelectedStarredHistoryCount = 0;
        UpdateAreSelectedRecordsStarred();
    }

    private void SetSelectedRecordStarredState(HistoryRecordKey key, bool starred)
    {
        if (!selectedHistoryRecords.TryGetValue(key, out var recordInfo))
            return;

        if (recordInfo.Starred == starred)
            return;
        SelectedStarredHistoryCount += starred ? 1 : -1;

        recordInfo.Starred = starred;
        UpdateAreSelectedRecordsStarred();
    }

    private void SetSelectedRecordsStarredState(IEnumerable<HistoryRecordKey> keys, bool starred)
    {
        foreach (var key in keys)
            SetSelectedRecordStarredState(key, starred);
    }

    private void UpdateAreSelectedRecordsStarred() =>
        AreSelectedRecordsStarred = SelectedHistoryCount > 0 && SelectedStarredHistoryCount == SelectedHistoryCount;

    private bool HasSameSelectedHistoryKeys(IEnumerable<HistoryRecordKey> keys) =>
        selectedHistoryRecords.Count == keys.Count() && keys.All(selectedHistoryRecords.ContainsKey);

    private void SetVisibleRecordSelection(HistoryRecordVM record, bool isSelected)
    {
        record.IsSelected = isSelected;
        var index = visibleSelectedItems.IndexOf(record);
        if (isSelected && index < 0)
            visibleSelectedItems.Add(record);
        else if (!isSelected && index >= 0)
            visibleSelectedItems.RemoveAt(index);
    }

    private void RefreshVisibleSelectedItems()
    {
        foreach (var record in visibleSelectedItems.ToArray())
        {
            if (!HistoryItems.Any(item => ReferenceEquals(item, record))
                || !selectedHistoryRecords.ContainsKey(record.Key))
                SetVisibleRecordSelection(record, false);
        }

        foreach (var record in HistoryItems)
        {
            if (selectedHistoryRecords.ContainsKey(record.Key))
                SetVisibleRecordSelection(record, true);
        }
    }

    private void ApplySelectionToRecord(HistoryRecordVM record)
    {
        record.IsSelected = selectedHistoryRecords.ContainsKey(record.Key);
        SetSelectedRecordStarredState(record.Key, record.Stared);
    }

    private void RefreshSelectionSummaryForUpdatedRecord(HistoryRecordVM record)
    {
        if (IsMultiSelecting
            && selectedHistoryRecords.ContainsKey(record.Key)
            && SelectedFilter == HistoryFilterType.Transferring)
            RequestSelectionSummaryRefresh(SelectionSummaryPart.Counts);
    }

    private void RemoveRecordFromSelection(HistoryRecord record)
    {
        var key = HistoryRecordKey.From(record);
        RemoveSelectedHistoryKey(key);
        if (selectionAnchor == key)
            selectionAnchor = null;

        var visibleRecord = allHistoryItems.FirstOrDefault(item => item.Key == key);
        if (visibleRecord is not null)
            SetVisibleRecordSelection(visibleRecord, false);
        RequestSelectionSummaryRefresh(SelectionSummaryPart.Counts);
    }

    private void ApplyHistoryRecordRemoval(HistoryRecord record)
    {
        var key = HistoryRecordKey.From(record);
        var visibleRecord = allHistoryItems.FirstOrDefault(item => item.Key == key);
        var selectionAfterRemoval = visibleRecord is not null
            ? PrepareSelectionForRecordRemoval(visibleRecord)
            : null;

        RemoveRecordFromSelection(record);
        if (visibleRecord is not null)
            allHistoryItems.Remove(visibleRecord);

        if (selectionAfterRemoval is not null)
            SelectSingleRecord(selectionAfterRemoval);
    }

    private void ClearVisibleRecordSelection(HistoryRecordVM record) =>
        SetVisibleRecordSelection(record, false);

    private void ClearSelectedItem() => SelectedItem = null;

    private void ClearVisibleSelectedItems()
    {
        foreach (var record in visibleSelectedItems.ToArray())
            SetVisibleRecordSelection(record, false);
    }
}
