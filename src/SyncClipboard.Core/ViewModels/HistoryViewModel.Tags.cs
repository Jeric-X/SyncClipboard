using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        Count = count;
        this.showCount = showCount;
        SelectCommand = new RelayCommand(() => select(this));
    }

    public string? Name { get; }
    public int Count { get; }
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
    private IReadOnlyList<HistoryTagFilter> tagOptions = [];

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

    public bool IsTagBarVisible => ShowTagBar && SelectedFilter != HistoryFilterType.Transferring;

    public void ToggleTagFilter(HistoryTagFilter option)
    {
        SelectedTags = option.Name is null || SelectedTags.Contains(option.Name, StringComparer.Ordinal)
            ? [] : [option.Name];
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
            TagOptions = counts.Keys.Concat(selected).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .Select(name => new HistoryTagFilter(name, counts.GetValueOrDefault(name), ShowTagCounts, ToggleTagFilter))
                .Prepend(new HistoryTagFilter(null, 0, ShowTagCounts, ToggleTagFilter)).ToArray();
            UpdateTagSelection();
        });
    }
}
