using System.Collections.Concurrent;
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
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.Utilities.Keyboard;
using SyncClipboard.Core.Utilities.Runner;
using SyncClipboard.Core.ViewModels.Sub;
using SyncClipboard.Server.Core.Models;

namespace SyncClipboard.Core.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private IWindow window = null!;

    [ObservableProperty]
    private bool showInfoBar = false;

    [ObservableProperty]
    private string infoBarMessage = string.Empty;

    private CancellationTokenSource? infoBarCancellationSource;

    private readonly HistoryManager historyManager;
    private readonly VirtualKeyboard keyboard;
    private readonly ConfigBase runtimeConfig;
    private readonly ConfigManager _configManager;
    private readonly ILogger logger;
    private readonly LocalClipboardSetter localClipboardSetter;
    private readonly ProfileActionBuilder profileActionBuilder;
    private readonly RemoteClipboardServerFactory remoteServerFactory;
    private IHistorySyncServer? historySyncServer;

    private bool _enableSyncHistory;

    private readonly ThreadSafeDeque<DateTime?> _remoteFetchQueue = new();
    private readonly SemaphoreSlim _remoteWorkerSemaphore = new(1, 1);
    private CancellationTokenSource _remoteQueueCts = new();
    private readonly ConcurrentQueue<string> _serverUpdateQueue = new();
    private readonly ConcurrentDictionary<string, HistoryRecordDto> _serverUpdateMap = new();
    private int _serverUpdateWorkerRunning = 0;
    private readonly CancellationTokenSource _serverUpdateCts = new();

    public HistoryViewModel(
        HistoryManager historyManager,
        VirtualKeyboard keyboard,
        [FromKeyedServices(Env.RuntimeConfigName)] ConfigBase runtimeConfig,
        ConfigManager configManager,
        ILogger logger,
        LocalClipboardSetter localClipboardSetter,
        ProfileActionBuilder profileActionBuilder,
        RemoteClipboardServerFactory remoteServerFactory)
    {
        this.historyManager = historyManager;
        this.keyboard = keyboard;
        this.runtimeConfig = runtimeConfig;
        this._configManager = configManager;
        this.logger = logger;
        this.localClipboardSetter = localClipboardSetter;
        this.profileActionBuilder = profileActionBuilder;
        this.remoteServerFactory = remoteServerFactory;

        var currentServer = remoteServerFactory.Current;
        _enableSyncHistory = configManager.GetConfig<HistoryConfig>().EnableSyncHistory;
        historySyncServer = _enableSyncHistory ? currentServer as IHistorySyncServer : null;

        _configManager.ListenConfig<HistoryConfig>(OnHistoryConfigChanged);

        viewController = allHistoryItems.CreateView(x => x);
        HistoryItems = viewController.ToNotifyCollectionChanged();
        ApplyFilter();
    }

    private void OnHistoryConfigChanged(HistoryConfig cfg)
    {
        var newEnable = cfg.EnableSyncHistory;
        if (newEnable == _enableSyncHistory)
            return;

        _enableSyncHistory = newEnable;
        if (_enableSyncHistory)
        {
            historySyncServer = remoteServerFactory.Current as IHistorySyncServer;
            _ = Refresh();
        }
        else
        {
            historySyncServer = null;
            CancelFetchTask();
            _isRemoteEnd = true;
        }
    }

    private readonly object _remoteQueueCtsLock = new();
    private void CancelFetchTask()
    {
        _remoteFetchQueue.Clear();
        lock (_remoteQueueCtsLock)
        {
            var oldCts = _remoteQueueCts;
            _remoteQueueCts = new CancellationTokenSource();
            oldCts.Cancel();
            oldCts.Dispose();
        }
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
    private string? _localIdCursor = null;
    private DateTime? _localTimeCursor = null;
    private DateTime? _remoteTimeCursor = null;
    private string? _remoteIdCursor = null;
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

    private int _isLoadTaskRunning = 0;
    private double _lastOffsetY = 0;
    private double _lastViewportHeight = 0;
    private double _lastExtentHeight = 0;

    public void SetScrollViewMetrics(double offsetY, double viewportHeight, double extentHeight)
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
        SetScrollViewMetrics(offsetY, viewportHeight, extentHeight);
        if (IsEnd) return;
        if (extentHeight <= 0) return;

        if (IsScrollViewerEnabled() && offsetY + viewportHeight < 0.8 * extentHeight) return;

        if (_isLoadTaskRunning != 0) return;

        await RunLoadTask(MorePageSize);
    }

    private async Task RunLoadTask(int size)
    {
        if (window is null) return;
        if (Interlocked.CompareExchange(ref _isLoadTaskRunning, 1, 0) != 0) return;
        using var scopeGuard = new ScopeGuard(() => Interlocked.Exchange(ref _isLoadTaskRunning, 0));

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
                        SetScrollViewMetrics(offsetY, viewportHeight, extentHeight);
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


    public bool IsLoading => IsLoadingLocal || IsLoadingRemote;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    private bool isLoadingLocal = false;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    private bool isLoadingRemote = false;

    private bool _localIsEnd = false;
    private bool _isRemoteEnd = false;
    private bool IsEnd => _localIsEnd && _isRemoteEnd;

    [RelayCommand]
    public Task Refresh()
    {
        CancelFetchTask();
        _localIsEnd = false;
        _isRemoteEnd = false;

        allHistoryItems.Clear();
        _localIdCursor = null;
        _localTimeCursor = null;
        _remoteTimeCursor = null;
        _remoteIdCursor = null;
        _lastViewportHeight = 0;
        _lastExtentHeight = 0;
        window?.ScrollToTop();
        return RunLoadTask(InitialPageSize);
    }

    private (ProfileTypeFilter types, bool? starred, string? searchText) BuildQueryParameters()
    {
        bool? starred = null;
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
                starred = true;
                break;
            case HistoryFilterType.All:
            default:
                types = ProfileTypeFilter.All;
                break;
        }

        return (types, starred, searchText);
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

        historyManager.HistoryAdded += RecordUpdated;
        historyManager.HistoryUpdated += RecordUpdated;
        historyManager.HistoryRemoved += record => allHistoryItems.Remove(new HistoryRecordVM(record));

        remoteServerFactory.CurrentServerChanged += (sender, e) => OnRemoteServerChanged();
    }

    private void RecordUpdated(HistoryRecord record)
    {
        var newRecord = new HistoryRecordVM(record);
        var oldRecord = allHistoryItems.FirstOrDefault(r => r == newRecord);
        if (!IsMatchCurrentUiFilter(newRecord))
        {
            if (oldRecord != null)
            {
                allHistoryItems.Remove(oldRecord);
            }
            return;
        }

        if (oldRecord == null)
        {
            InsertHistoryInOrder(record);
            return;
        }
        oldRecord.Update(newRecord);
    }

    private bool IsMatchCurrentUiFilter(HistoryRecordVM vm)
    {
        bool typeMatch = SelectedFilter switch
        {
            HistoryFilterType.All => true,
            HistoryFilterType.Text => vm.Type == ProfileType.Text,
            HistoryFilterType.Image => vm.Type == ProfileType.Image,
            HistoryFilterType.File => vm.Type == ProfileType.File || vm.Type == ProfileType.Group,
            HistoryFilterType.Starred => vm.Stared,
            _ => true
        };

        if (!typeMatch)
            return false;

        if (!string.IsNullOrEmpty(SearchText))
        {
            if (!vm.Text.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    // 对有序队列实行二分查找
    private void InsertHistoryInOrder(HistoryRecord record)
    {
        var vm = new HistoryRecordVM(record);
        if (allHistoryItems.Count == 0)
        {
            allHistoryItems.Insert(0, vm);
            return;
        }

        int low = 0, high = allHistoryItems.Count;
        var t = vm.Timestamp;
        while (low < high)
        {
            int mid = (low + high) >> 1;
            var midT = allHistoryItems[mid].Timestamp;
            if (midT <= t)
            {
                high = mid;
            }
            else
            {
                low = mid + 1;
            }
        }
        allHistoryItems.Insert(low, vm);
    }

    private void OnRemoteServerChanged()
    {
        var currentServer = remoteServerFactory.Current;
        historySyncServer = _enableSyncHistory ? currentServer as IHistorySyncServer : null;
        _ = Refresh();

        if (historySyncServer != null)
        {
            logger.Write("[HISTORY_VIEW_MODEL] Remote server changed, historySyncServer is now available");
        }
        else
        {
            logger.Write("[HISTORY_VIEW_MODEL] Remote server changed, historySyncServer is not available");
        }
    }

    private async Task LoadRemotePage()
    {
        if (historySyncServer == null)
        {
            _isRemoteEnd = true;
            return;
        }

        _remoteFetchQueue.EnqueueTail(null);
        try
        {
            await ProcessRemoteQueueAsync(waitAllTasks: true);
        }
        catch { }
    }

    private async Task DoLoadPageAsync(int size, CancellationToken token)
    {
        var (types, started, searchText) = BuildQueryParameters();

        if (_localIsEnd)
        {
            await LoadRemotePage();
            return;
        }

        IsLoadingLocal = true;
        var records = await historyManager.GetHistoryAsync(
            types,
            started,
            _localTimeCursor,
            _localIdCursor,
            size,
            string.IsNullOrEmpty(searchText) ? null : searchText,
            token);

        _localIsEnd = records == null || records.Count == 0;
        if (_localIsEnd)
        {
            IsLoadingLocal = false;
            await LoadRemotePage();
            return;
        }

        var vms = records!.Select(x => new HistoryRecordVM(x)).ToList();
        allHistoryItems.AddRange(vms);

        var last = vms.LastOrDefault();
        if (last != null)
        {
            _localIdCursor = $"{last.Type}-{last.Hash}";
            _remoteFetchQueue.EnqueueTail(last.Timestamp);
            _ = ProcessRemoteQueueAsync();
            _localTimeCursor = last.Timestamp;
        }

        _localIsEnd = vms.Count < size;
        IsLoadingLocal = false;
    }

    private static long ToUnixMilliseconds(DateTime time)
    {
        var utc = DateTime.SpecifyKind(time, DateTimeKind.Utc);
        return new DateTimeOffset(utc).ToUnixTimeMilliseconds();
    }

    private async Task<bool> FetchAndSyncRemotePageAsync(long? after, int page, CancellationToken ct)
    {
        if (historySyncServer == null)
            return false;

        long? before = _remoteTimeCursor.HasValue ? ToUnixMilliseconds(_remoteTimeCursor.Value) : null;
        if (after.HasValue && before.HasValue && after.Value >= before.Value)
        {
            before = null;
        }

        var (types, starred, searchText) = BuildQueryParameters();
        var list = await historySyncServer.GetHistoryAsync(
            page: page,
            before: before,
            after: after,
            cursorProfileId: _remoteIdCursor,
            types: types,
            searchText: string.IsNullOrEmpty(searchText) ? null : searchText,
            starred: starred);

        if (list.Any() == false)
        {
            if (after is null)
            {
                _isRemoteEnd = true;
            }
            return false;
        }

        var needUpload = await historyManager.SyncRemoteHistoryAsync(list, ct);
        foreach (var dto in needUpload)
        {
            var pid = $"{dto.Type}-{dto.Hash}";
            _serverUpdateMap[pid] = dto;
            _serverUpdateQueue.Enqueue(pid);
        }
        _ = ProcessServerUpdateQueueAsync();

        var lastRemote = list.Last();
        _remoteTimeCursor = lastRemote.CreateTime;
        _remoteIdCursor = $"{lastRemote.Type}-{lastRemote.Hash}";
        return true;
    }

    private async Task ProcessRemoteQueueAsync(bool waitAllTasks = false)
    {
        if (historySyncServer == null || _isRemoteEnd)
        {
            _isRemoteEnd = true;
            return;
        }

        var ct = _remoteQueueCts.Token;
        if (waitAllTasks)
        {
            await _remoteWorkerSemaphore.WaitAsync(ct);
        }
        else if (!_remoteWorkerSemaphore.Wait(0))
        {
            return;
        }

        IsLoadingRemote = true;
        int errorCount = 0;
        try
        {
            while (errorCount <= 5 && !ct.IsCancellationRequested && _remoteFetchQueue.TryDequeue(out var afterDate))
            {
                try
                {
                    var after = afterDate.HasValue ? ToUnixMilliseconds(afterDate.Value) : (long?)null;
                    var maxPage = after is null ? 1 : int.MaxValue;
                    for (int page = 1; page <= maxPage && !ct.IsCancellationRequested; page++)
                    {
                        var success = await FetchAndSyncRemotePageAsync(after, page, ct);
                        errorCount = 0;
                        if (!success)
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex) when (ct.IsCancellationRequested == false)
                {
                    logger.Write("[HISTORY_VIEW_MODEL] Remote fetch failed:", ex.Message);
                    errorCount++;
                    _remoteFetchQueue.EnqueueHead(afterDate);
                }
            }
        }
        finally
        {
            IsLoadingRemote = false;
            _remoteWorkerSemaphore.Release();
            if (errorCount == 0 && !_remoteFetchQueue.IsEmpty && !ct.IsCancellationRequested)
            {
                await ProcessRemoteQueueAsync(waitAllTasks);
            }
        }
    }

    private async Task ProcessServerUpdateQueueAsync()
    {
        if (Interlocked.CompareExchange(ref _serverUpdateWorkerRunning, 1, 0) != 0)
            return;
        try
        {
            int errorCount = 0;
            var ct = _serverUpdateCts.Token;
            while (errorCount <= 5 && !ct.IsCancellationRequested && _serverUpdateQueue.TryDequeue(out var pid))
            {
                try
                {
                    if (_serverUpdateMap.TryGetValue(pid, out var latestDto))
                    {
                        // TODO: once upload API is available, invoke it here with latestDto
                        logger.Write("[HISTORY_VIEW_MODEL] Uploading to server:", pid);
                        await Task.Yield();
                        _serverUpdateMap.TryRemove(pid, out _);
                        errorCount = 0;
                    }
                }
                catch (Exception ex) when (ct.IsCancellationRequested == false)
                {
                    logger.Write("[HISTORY_VIEW_MODEL] Server update failed, re-enqueue:", ex.Message);
                    errorCount++;
                    _serverUpdateQueue.Enqueue(pid);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _serverUpdateWorkerRunning, 0);
        }
    }

    private void DownloadRemoteProfile(HistoryRecordVM _)
    {
        // todo: implement download logic
        logger.Write("[HISTORY_VIEW_MODEL] Downloading remote profile is not implemented yet.");
    }

    public async Task<List<MenuItem>> BuildActionsAsync(HistoryRecordVM record)
    {
        if (record.SyncState == SyncStatus.ServerOnly)
        {
            return
            [
                new MenuItem(I18n.Strings.DeleteHistory, () => { _ = historyManager.DeleteHistory(record.ToHistoryRecord()); }),
                new MenuItem(I18n.Strings.Download, () => DownloadRemoteProfile(record))
            ];
        }

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