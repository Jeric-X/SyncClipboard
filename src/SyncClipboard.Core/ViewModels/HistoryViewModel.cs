using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ObservableCollections;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.Utilities.Keyboard;
using SyncClipboard.Core.Utilities.Runner;
using SyncClipboard.Core.ViewModels.Sub;

namespace SyncClipboard.Core.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private IWindow window = null!;

    [ObservableProperty]
    private bool showInfoBar = false;

    [ObservableProperty]
    private string infoBarMessage = string.Empty;

    private CancellationTokenSource? infoBarCancellationSource;

    private readonly ConfigManager configManager;
    private readonly HistoryManager historyManager;
    private readonly IClipboardFactory clipboardFactory;
    private readonly VirtualKeyboard keyboard;
    private readonly ConfigBase runtimeConfig;
    private readonly ILogger logger;
    private readonly LocalClipboardSetter localClipboardSetter;
    private readonly ProfileActionBuilder profileActionBuilder;

    public HistoryViewModel(
        ConfigManager configManager,
        HistoryManager historyManager,
        IClipboardFactory clipboardFactory,
        VirtualKeyboard keyboard,
        [FromKeyedServices(Env.RuntimeConfigName)] ConfigBase runtimeConfig,
        ILogger logger,
        LocalClipboardSetter localClipboardSetter,
        ProfileActionBuilder profileActionBuilder)
    {
        this.configManager = configManager;
        this.historyManager = historyManager;
        this.clipboardFactory = clipboardFactory;
        this.keyboard = keyboard;
        this.runtimeConfig = runtimeConfig;
        this.logger = logger;
        this.localClipboardSetter = localClipboardSetter;
        this.profileActionBuilder = profileActionBuilder;

        viewController = allHistoryItems.CreateView(x => x);
        HistoryItems = viewController.ToNotifyCollectionChanged();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        viewController.AttachFilter(record =>
        {
            if (OnlyShowLocal && record.SyncState == SyncStatus.ServerOnly)
                return false;
            return true;
        });
        window?.ScrollToTop();
    }

    [ObservableProperty]
    private int selectedIndex = -1;

    [ObservableProperty]
    private HistoryFilterType selectedFilter = HistoryFilterType.All;
    partial void OnSelectedFilterChanged(HistoryFilterType value)
    {
        _ = Refresh();
        OnPropertyChanged(nameof(SelectedFilterOption));
    }


    [ObservableProperty]
    private string searchText = string.Empty;
    partial void OnSearchTextChanged(string value)
    {
        _ = Refresh();
    }

    public LocaleString<HistoryFilterType> SelectedFilterOption
    {
        get => FilterOptions.FirstOrDefault(x => x.Key.Equals(SelectedFilter)) ?? FilterOptions[0];
        set
        {
            if (value != null)
            {
                SelectedFilter = value.Key;
            }
        }
    }

    public List<LocaleString<HistoryFilterType>> FilterOptions { get; } =
        [
            new(HistoryFilterType.All, I18n.Strings.HistoryFilterAll),
            new(HistoryFilterType.Text, I18n.Strings.HistoryFilterText),
            new(HistoryFilterType.Image, I18n.Strings.HistoryFilterImage),
            new(HistoryFilterType.File, I18n.Strings.HistoryFilterFile),
            new(HistoryFilterType.Starred, I18n.Strings.HistoryFilterStarred)
        ];

    public INotifyCollectionChangedSynchronizedViewList<HistoryRecordVM> HistoryItems { get; }
    private readonly ISynchronizedView<HistoryRecordVM, HistoryRecordVM> viewController;
    private readonly ObservableList<HistoryRecordVM> allHistoryItems = [];

    private const int InitialPageSize = 20;
    private const int MorePageSize = 20;
    private string? _cursorProfileId = null;
    private DateTime? _cursorProfileTime = null;
    private readonly SingletonTask _loader = new SingletonTask();

    public int Width
    {
        get => runtimeConfig.GetConfig<HistoryWindowConfig>().Width;
        set => runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { Width = value });
    }

    public int Height
    {
        get => runtimeConfig.GetConfig<HistoryWindowConfig>().Height;
        set => runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { Height = value });
    }

    public bool IsTopmost
    {
        get => runtimeConfig.GetConfig<HistoryWindowConfig>().IsTopmost;
        set
        {
            window?.SetTopmost(value);
            runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { IsTopmost = value });
        }
    }

    public bool ScrollToTopOnReopen
    {
        get => runtimeConfig.GetConfig<HistoryWindowConfig>().ScrollToTopOnReopen;
        set => runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { ScrollToTopOnReopen = value });
    }

    public bool CloseWhenLostFocus
    {
        get => runtimeConfig.GetConfig<HistoryWindowConfig>().CloseWhenLostFocus;
        set => runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { CloseWhenLostFocus = value });
    }

    public bool ShowSyncState
    {
        get => runtimeConfig.GetConfig<HistoryWindowConfig>().ShowSyncState;
        set
        {
            runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { ShowSyncState = value });
            OnPropertyChanged(nameof(ShowSyncState));
        }
    }

    [ObservableProperty]
    private bool serverConnected = true;

    public bool OnlyShowLocal
    {
        get => runtimeConfig.GetConfig<HistoryWindowConfig>().OnlyShowLocal;
        set
        {
            runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { OnlyShowLocal = value });
            // Re-apply filter so UI reflects the change immediately
            ApplyFilter();
            OnPropertyChanged(nameof(OnlyShowLocal));
        }
    }

    [RelayCommand]
    public Task DeleteItem(HistoryRecordVM record)
    {
        return historyManager.DeleteHistory(record.ToHistoryRecord());
    }

    [RelayCommand]
    public Task ChangeStarStatus(HistoryRecordVM record)
    {
        record.Stared = !record.Stared;
        return historyManager.UpdateHistory(record.ToHistoryRecord());
    }

    [RelayCommand]
    public Task ChangePinStatus(HistoryRecordVM record)
    {
        record.Pinned = !record.Pinned;
        return historyManager.UpdateHistory(record.ToHistoryRecord());
    }

    [RelayCommand]
    public void CtrlHome()
    {
        ScrollToTop();
    }

    [RelayCommand]
    public void CtrlEnd()
    {
        ScrollToBottom();
    }

    [RelayCommand]
    public void Close()
    {
        window?.Close();
    }

    private int _isTriggeringLoadMore = 0;
    private double _lastOffsetY = 0;
    private double _lastViewportHeight = 0;
    private double _lastExtentHeight = 0;

    public void SetScollViewMetrics(double offsetY, double viewportHeight, double extentHeight)
    {
        _lastOffsetY = offsetY;
        _lastViewportHeight = viewportHeight;
        _lastExtentHeight = extentHeight;
    }

    private bool IsScrollViewerEnabled()
    {
        return _lastViewportHeight > 0 && _lastExtentHeight > _lastViewportHeight + 0.1;
    }

    public async Task NotifyScrollPositionAsync(double offsetY, double viewportHeight, double extentHeight)
    {
        try
        {
            SetScollViewMetrics(offsetY, viewportHeight, extentHeight);
            if (IsEnd) return;
            if (extentHeight <= 0) return;

            if (IsScrollViewerEnabled() && offsetY + viewportHeight < 0.8 * extentHeight) return;

            if (IsLoadingMore) return;

            // 避免并发触发
            if (Interlocked.CompareExchange(ref _isTriggeringLoadMore, 1, 0) != 0) return;
            try
            {
                await RunLoadTask(MorePageSize);
            }
            catch { }
            finally
            {
                Interlocked.Exchange(ref _isTriggeringLoadMore, 0);
            }
        }
        catch { }
    }

    private async Task RunLoadTask(int size)
    {
        IsLoadingMore = true;
        using var guard = new ScopeGuard(() => IsLoadingMore = false);
        if (IsEnd) return;
        if (window is null) return;

        await _loader.Run(async ct =>
        {
            try
            {
                await DoLoadPageAsync(size, ct);
                while (!IsEnd && ct.IsCancellationRequested == false && _lastExtentHeight <= _lastViewportHeight)
                {
                    await DoLoadPageAsync(size, ct);
                    if (window.GetScrollViewMetrics(out var offsetY, out var viewportHeight, out var extentHeight))
                    {
                        SetScollViewMetrics(offsetY, viewportHeight, extentHeight);
                    }
                    await Task.Delay(10, ct);
                }
            }
            catch (Exception ex)
            {
                logger.Write("Failed to load more history:", ex.Message);
            }
        });
    }

    [ObservableProperty]
    private bool isLoadingMore = false;

    [ObservableProperty]
    private bool isEnd = false;

    [RelayCommand]
    public Task Refresh()
    {
        IsEnd = false;
        allHistoryItems.Clear();
        _cursorProfileId = null;
        _cursorProfileTime = null;
        _lastViewportHeight = 0;
        _lastExtentHeight = 0;
        window?.ScrollToTop();
        return RunLoadTask(InitialPageSize);
    }

    private (ProfileTypeFilter types, bool? started, string? searchText) BuildQueryParameters()
    {
        bool? started = null;
        string? searchText = string.IsNullOrEmpty(SearchText) ? null : SearchText;

        ProfileTypeFilter types;
        switch (SelectedFilter)
        {
            case HistoryFilterType.Text:
                types = ProfileTypeFilter.Text;
                break;
            case HistoryFilterType.Image:
                types = ProfileTypeFilter.Image;
                break;
            case HistoryFilterType.File:
                types = ProfileTypeFilter.File | ProfileTypeFilter.Group;
                break;
            case HistoryFilterType.Starred:
                types = ProfileTypeFilter.All;
                started = true;
                break;
            case HistoryFilterType.All:
            default:
                types = ProfileTypeFilter.All;
                break;
        }

        return (types, started, searchText);
    }

    public void NavigateToNextFilter()
    {
        var currentIndex = (int)SelectedFilter;
        var filterCount = FilterOptions.Count;
        var nextIndex = (currentIndex + 1) % filterCount;
        SelectedFilter = (HistoryFilterType)nextIndex;
    }

    public void NavigateToPreviousFilter()
    {
        var currentIndex = (int)SelectedFilter;
        var filterCount = FilterOptions.Count;
        var prevIndex = (currentIndex - 1 + filterCount) % filterCount;
        SelectedFilter = (HistoryFilterType)prevIndex;
    }

    public void NavigateDown()
    {
        var count = ((ICollection<HistoryRecordVM>)HistoryItems).Count;
        if (count == 0) return;

        var maxIndex = count - 1;
        if (SelectedIndex < maxIndex)
        {
            SelectedIndex++;
        }
        window?.ScrollToSelectedItem();
    }

    public void NavigateUp()
    {
        var count = ((ICollection<HistoryRecordVM>)HistoryItems).Count;
        if (count == 0) return;

        if (SelectedIndex > 0)
        {
            SelectedIndex--;
        }
        window?.ScrollToSelectedItem();
    }

    public void NavigateToFirst()
    {
        var count = ((ICollection<HistoryRecordVM>)HistoryItems).Count;
        if (count == 0) return;

        SelectedIndex = 0;
        window?.ScrollToSelectedItem();
    }

    public void OnWindowShown()
    {
        if (ScrollToTopOnReopen)
        {
            ScrollToTop();
        }
    }

    public void NavigateToLast()
    {
        var count = ((ICollection<HistoryRecordVM>)HistoryItems).Count;
        if (count == 0) return;

        SelectedIndex = count - 1;
        window?.ScrollToSelectedItem();
    }

    public bool HandleKeyPress(Key key, bool isShiftPressed = false, bool isAltPressed = false, bool isCtrlPressed = false)
    {
        if (isCtrlPressed)
        {
            switch (key)
            {
                case Key.Home:
                    ScrollToTop();
                    return true;
                case Key.End:
                    ScrollToBottom();
                    return true;
            }
        }

        switch (key)
        {
            case Key.Tab:
                if (isShiftPressed)
                    NavigateToPreviousFilter();
                else
                    NavigateToNextFilter();
                return true;

            case Key.Down:
                NavigateDown();
                return true;

            case Key.Up:
                NavigateUp();
                return true;

            case Key.Enter:
                HandleEnterKey(isAltPressed);
                return true;

            case Key.Esc:
                window?.Close();
                return true;

            default:
                return false;
        }
    }

    private async void HandleEnterKey(bool isAltPressed)
    {
        var count = ((ICollection<HistoryRecordVM>)HistoryItems).Count;
        if (SelectedIndex < 0 || SelectedIndex >= count)
            return;

        var selectedItem = ((IList<HistoryRecordVM>)HistoryItems)[SelectedIndex];
        if (selectedItem == null) return;

        // Alt键表示不粘贴到剪贴板，只是复制操作
        var paste = !isAltPressed;
        await CopyToClipboard(selectedItem, paste, CancellationToken.None);
    }

    public void ViewImage(HistoryRecordVM record)
    {
        if (record.FilePath.Length == 0 || !File.Exists(record.FilePath[0]))
        {
            return;
        }
        _remainWindowForViewDetail = true;
        Sys.OpenWithDefaultApp(record.FilePath[0]);
    }

    [RelayCommand]
    public void ScrollToTop()
    {
        if (HistoryItems.Any())
        {
            SelectedIndex = 0;
            window?.ScrollToSelectedItem();
        }
    }

    private void ScrollToBottom()
    {
        if (HistoryItems.Any())
        {
            SelectedIndex = ((IList<HistoryRecordVM>)HistoryItems).Count - 1;
            window?.ScrollToSelectedItem();
        }
    }

    public async Task Init(IWindow window)
    {
        this.window = window;
        await Refresh();

        historyManager.HistoryAdded += record => allHistoryItems.Insert(0, new HistoryRecordVM(record));
        historyManager.HistoryRemoved += record => allHistoryItems.Remove(new HistoryRecordVM(record));
        historyManager.HistoryUpdated += record =>
        {
            var newRecord = new HistoryRecordVM(record);
            var oldRecord = allHistoryItems.FirstOrDefault(r => r == newRecord);
            if (oldRecord == null)
            {
                return;
            }
            oldRecord.Update(newRecord);
        };
    }

    private async Task DoLoadPageAsync(int size, CancellationToken token)
    {
        var (types, started, searchText) = BuildQueryParameters();
        List<HistoryRecord>? records = null;

        records = await historyManager.GetHistoryAsync(
            types,
            started,
            _cursorProfileTime,
            _cursorProfileId,
            size,
            string.IsNullOrEmpty(searchText) ? null : searchText,
            token);

        if (records == null || records.Count == 0)
        {
            IsEnd = true;
            return;
        }
        var vms = records.Select(x => new HistoryRecordVM(x)).ToList();
        allHistoryItems.AddRange(vms);

        var last = vms.LastOrDefault();
        if (last != null)
        {
            _cursorProfileId = $"{last.Type}-{last.Hash}";
            _cursorProfileTime = last.Timestamp;
        }

        if (records.Count < size)
        {
            IsEnd = true;
        }
    }

    public async Task<List<MenuItem>> BuildActionsAsync(HistoryRecordVM record)
    {
        var profile = record.ToHistoryRecord().ToProfile();
        var valid = await profile.IsLocalDataValid(true, CancellationToken.None);

        if (!valid)
        {
            var historyRecord = record.ToHistoryRecord();
            return
            [
                new MenuItem(I18n.Strings.DeleteHistory, () => { _ = historyManager.DeleteHistory(historyRecord); })
            ];
        }

        return profileActionBuilder.Build(profile);
    }

    public async Task CopyToClipboard(HistoryRecordVM record, bool paste, CancellationToken token)
    {
        var profile = record.ToHistoryRecord().ToProfile();
        var valid = await profile.IsLocalDataValid(true, token);
        if (!valid)
        {
            InfoBarMessage = I18n.Strings.UnableToCopyByMissingFile;
            ShowInfoBar = true;

            infoBarCancellationSource?.Cancel();
            infoBarCancellationSource = new CancellationTokenSource();
            _ = HideInfoBarAfterDelayAsync(infoBarCancellationSource.Token);
            return;
        }

        if (paste || !IsTopmost)
        {
            SelectedIndex = -1;
            window.ScrollToTop();
            window.Close();
        }

        await localClipboardSetter.Set(profile, token);
        if (paste)
        {
            keyboard.Paste();
        }
    }

    public void OnGotFocus()
    {
        _remainWindowForViewDetail = false;
    }

    private bool _remainWindowForViewDetail = false;

    public void OnLostFocus()
    {
        if (!_remainWindowForViewDetail && !IsTopmost && CloseWhenLostFocus)
        {
            window.Close();
        }
    }

    public void HandleItemDoubleClick(HistoryRecordVM record)
    {
        _ = CopyToClipboard(record, false, CancellationToken.None);
    }

    public void HandleImageDoubleClick(HistoryRecordVM record)
    {
        ViewImage(record);
    }

    private async Task HideInfoBarAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(4000, cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                ShowInfoBar = false;
            }
        }
        catch (OperationCanceledException)
        {
            // 任务被取消，这是预期的行为
        }
    }
}