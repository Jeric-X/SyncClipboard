using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Runner;
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
                SelectedTags = [SelectedTags[0]];
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

    private void UpdateTagSelection()
    {
        var selected = SelectedTags;
        foreach (var option in TagOptions ?? [])
            option.IsSelected = option.Name is null ? selected.Length == 0 : selected.Contains(option.Name, StringComparer.Ordinal);
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
        await _threadDispatcher.RunOnMainThreadAsync(() =>
        {
            var selected = SelectedTags;
            var names = counts.Keys.Concat(selected).ToHashSet(StringComparer.Ordinal);
            for (var i = TagOptions.Count - 1; i >= 0; i--)
            {
                if (TagOptions[i].Name is { } name && !names.Contains(name))
                    TagOptions.RemoveAt(i);
            }

            var index = 0;
            foreach (var name in names.Order(StringComparer.Ordinal).Cast<string?>().Prepend(null))
            {
                var count = name is null ? 0 : counts.GetValueOrDefault(name);
                var isSelected = name is null ? selected.Length == 0 : selected.Contains(name, StringComparer.Ordinal);
                if (index < TagOptions.Count && string.Equals(TagOptions[index].Name, name, StringComparison.Ordinal))
                {
                    var option = TagOptions[index];
                    option.Count = count;
                    option.ShowCount = ShowTagCounts;
                    option.IsSelected = isSelected;
                }
                else
                {
                    TagOptions.Insert(index, new HistoryTagFilter(name, count, ShowTagCounts, ToggleTagFilter)
                    {
                        IsSelected = isSelected
                    });
                }
                index++;
            }
        });
    }
}
