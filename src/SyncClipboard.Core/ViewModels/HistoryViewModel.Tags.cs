using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Runner;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.ViewModels.Sub;

namespace SyncClipboard.Core.ViewModels;

public sealed partial class HistoryTagFilter : ObservableObject
{
    public HistoryTagFilter(string? name, int count, bool showCount, Action<HistoryTagFilter> select)
    {
        Name = name;
        this.count = count;
        this.showCount = showCount;
        SelectCommand = new RelayCommand(() => select(this));
    }

    public string? Name { get; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text))]
    private int count;
    public string Text => Name is null ? Strings.HistoryFilterAll : ShowCount ? $"{Name} ({Count})" : Name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text))]
    private bool showCount;
    public IRelayCommand SelectCommand { get; }

    [ObservableProperty]
    private bool isSelected;
}

public partial class HistoryViewModel
{
    private CoalescingTask<bool>? _tagRefresh;

    [ObservableProperty]
    private ObservableCollection<HistoryTagFilter> tagOptions = [];

    public async Task RemoveRecordTagAsync(HistoryPreviewTag tag)
    {
        try
        {
            await historyManager.EditTagsAsync([tag.Record], [], [tag.Name]);
        }
        catch (Exception ex)
        {
            ShowWindowToastInfo(string.Format(Strings.OperationFailedRetryMessage, ex.Message));
        }
    }

    public bool ShowTagBar
    {
        get => runtimeConfig.GetConfig<HistoryWindowConfig>().ShowTagBar;
        set
        {
            if (value == ShowTagBar)
                return;
            runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { ShowTagBar = value });
            if (!value)
                SelectedTags = [];
            OnPropertyChanged(nameof(ShowTagBar));
            OnPropertyChanged(nameof(IsTagBarVisible));
        }
    }

    public bool ShowTagCounts
    {
        get => runtimeConfig.GetConfig<HistoryWindowConfig>().ShowTagCounts;
        set
        {
            if (value == ShowTagCounts)
                return;
            runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { ShowTagCounts = value });
            foreach (var option in TagOptions ?? [])
                option.ShowCount = value;
            OnPropertyChanged(nameof(ShowTagCounts));
        }
    }

    public bool MultiTagFilter
    {
        get => runtimeConfig.GetConfig<HistoryWindowConfig>().MultiTagFilter;
        set
        {
            if (value == MultiTagFilter)
                return;
            runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { MultiTagFilter = value });
            if (!value && SelectedTags.Length > 1)
            {
                var first = TagOptions.FirstOrDefault(option => option.Name is { } name
                    && SelectedTags.Contains(name, StringComparer.Ordinal))?.Name ?? SelectedTags[0];
                SelectedTags = [first];
            }
            OnPropertyChanged(nameof(MultiTagFilter));
        }
    }

    public bool IsTagBarVisible => ShowTagBar && SelectedFilter != HistoryFilterType.Transferring;

    public void ToggleTagFilter(HistoryTagFilter option)
    {
        var selected = SelectedTags;
        if (option.Name is null)
            SelectedTags = [];
        else if (!MultiTagFilter)
            SelectedTags = selected.Contains(option.Name, StringComparer.Ordinal) ? [] : [option.Name];
        else
            SelectedTags = selected.Contains(option.Name, StringComparer.Ordinal)
                ? selected.Where(tag => tag != option.Name).ToArray() : [.. selected, option.Name];
        // Clicking All can leave the query unchanged; restore its checked state as well.
        UpdateTagSelection();
    }

    private bool HandleTagShortcut(HistoryShortcutAction action)
    {
        if (!IsTagBarVisible || HasModalOperation)
            return false;

        switch (action)
        {
            case HistoryShortcutAction.PreviousTag:
            case HistoryShortcutAction.NextTag:
                window?.NavigateTagFilter(action == HistoryShortcutAction.NextTag);
                break;
            case HistoryShortcutAction.TagAbove:
            case HistoryShortcutAction.TagBelow:
                window?.NavigateTagFilterVertically(action == HistoryShortcutAction.TagBelow);
                break;
            case HistoryShortcutAction.ToggleTagsExpanded:
                window?.ToggleTagBarExpanded();
                break;
            default:
                return false;
        }
        return true;
    }

    public HistoryTagFilter? SelectAdjacentTag(IReadOnlyList<HistoryTagFilter> options, bool forward)
    {
        if (!IsTagBarVisible || HasModalOperation || options.Count == 0)
            return null;

        // Use the first selected tag in display order as the anchor for a multi-tag filter.
        var current = -1;
        for (var i = 0; i < options.Count; i++)
        {
            if (options[i].Name is { } name ? SelectedTags.Contains(name, StringComparer.Ordinal) : SelectedTags.Length == 0)
            {
                current = i;
                break;
            }
        }

        var next = current < 0 ? (forward ? 0 : options.Count - 1)
            : (current + (forward ? 1 : options.Count - 1)) % options.Count;
        var target = options[next];
        SelectedTags = target.Name is { } targetName ? [targetName] : [];
        return target;
    }

    public HistoryTagFilter? SelectTagInAdjacentRow(
        IReadOnlyList<(HistoryTagFilter Tag, double X, double Y, double Width)> options, bool down)
    {
        if (!IsTagBarVisible || HasModalOperation)
            return null;

        // Multi-tag filters use the first selected tag in display order, as horizontal navigation does.
        var current = options.FirstOrDefault(option => option.Tag.IsSelected);
        if (current.Tag is null)
            return null;

        var nextRow = options.Where(option => down ? option.Y > current.Y + 0.5 : option.Y < current.Y - 0.5)
            .OrderBy(option => Math.Abs(option.Y - current.Y)).FirstOrDefault();
        if (nextRow.Tag is null)
            return null;

        var center = current.X + (current.Width / 2);
        var target = options.Where(option => Math.Abs(option.Y - nextRow.Y) <= 0.5)
            .MinBy(option => Math.Abs(option.X + (option.Width / 2) - center)).Tag;
        SelectedTags = target.Name is { } name ? [name] : [];
        return target;
    }

    private void UpdateTagSelection()
    {
        var selected = SelectedTags;
        foreach (var option in TagOptions ?? [])
            option.IsSelected = option.Name is null ? selected.Length == 0 : selected.Contains(option.Name, StringComparer.Ordinal);
    }

    public string[] PinnedTags => runtimeConfig.GetConfig<HistoryWindowConfig>().PinnedTags;

    public async Task SetPinnedTagsAsync(IEnumerable<string> names)
    {
        var pinned = names.Select(HistoryTags.NormalizeName).Distinct(StringComparer.Ordinal).ToArray();
        runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { PinnedTags = pinned });
        if (!PinnedTags.SequenceEqual(pinned, StringComparer.Ordinal))
            throw new IOException(string.Format(Strings.SaveConfigFailed, runtimeConfig.Path));
        // Saving local pins must not wait for database refreshes driven by history synchronization.
        await _threadDispatcher.RunOnMainThreadAsync(() =>
        {
            foreach (var record in allHistoryItems)
                UpdateRecordTagOrder(record, pinned);
            var counts = TagOptions.Where(tag => tag.Name is not null && tag.Count > 0)
                .ToDictionary(tag => tag.Name!, tag => tag.Count, StringComparer.Ordinal);
            UpdateTagOptions(counts);
        });
    }

    private static void UpdateRecordTagOrder(HistoryRecordVM record, string[] pinnedTags)
    {
        var pinned = pinnedTags.Intersect(record.Tags, StringComparer.Ordinal);
        record.PreviewTags = HistoryTagOrdering.Order(record.Tags, pinned)
            .Select(name => new HistoryPreviewTag(record.Key, name)).ToArray();
    }

    private void InitializeTagRefresh()
    {
        _tagRefresh = new(
            (_, _) => true,
            (_, token) => RefreshTagOptionsCoreAsync(token),
            ex => logger.WriteAsync("Failed to refresh history tags", ex.Message));
    }

    public Task RefreshTagOptionsAsync() => _tagRefresh?.RunAsync(true) ?? Task.CompletedTask;

    private async Task RefreshTagOptionsCoreAsync(CancellationToken token)
    {
        // Merge bursts of history changes without canceling an in-flight database query.
        await Task.Delay(60, token);
        var counts = await Task.Run(() => historyManager.GetTagCountsAsync(token), token);
        await _threadDispatcher.RunOnMainThreadAsync(() => UpdateTagOptions(counts));
    }

    private void UpdateTagOptions(IReadOnlyDictionary<string, int> counts)
    {
        var selected = SelectedTags;
        var ordered = HistoryTagOrdering.Order(counts.Keys.Concat(selected), PinnedTags);
        var names = ordered.ToHashSet(StringComparer.Ordinal);
        for (var i = TagOptions.Count - 1; i >= 0; i--)
        {
            if (TagOptions[i].Name is { } name && !names.Contains(name))
                TagOptions.RemoveAt(i);
        }

        var index = 0;
        foreach (var name in ordered.Cast<string?>().Prepend(null))
        {
            var count = name is null ? 0 : counts.GetValueOrDefault(name);
            var option = GetOrMoveTagOption(name, index, count);
            option.Count = count;
            option.ShowCount = ShowTagCounts;
            option.IsSelected = name is null ? selected.Length == 0 : selected.Contains(name, StringComparer.Ordinal);
            index++;
        }
    }

    private HistoryTagFilter GetOrMoveTagOption(string? name, int index, int count)
    {
        if (index < TagOptions.Count && string.Equals(TagOptions[index].Name, name, StringComparison.Ordinal))
            return TagOptions[index];

        var option = TagOptions.FirstOrDefault(tag => string.Equals(tag.Name, name, StringComparison.Ordinal));
        if (option is null)
        {
            option = new HistoryTagFilter(name, count, ShowTagCounts, ToggleTagFilter);
            TagOptions.Insert(index, option);
        }
        else
        {
            TagOptions.Move(TagOptions.IndexOf(option), index);
        }
        return option;
    }
}
