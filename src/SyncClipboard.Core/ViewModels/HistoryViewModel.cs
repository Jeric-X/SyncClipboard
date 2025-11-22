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
using SyncClipboard.Core.Exceptions;
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
    private readonly HistorySyncer historySyncer;
    private IHistorySyncServer? historySyncServer;

    private bool _enableSyncHistory;

    private readonly ThreadSafeDeque<HistorySyncInfo> _remoteFetchQueue = new();
    private readonly SemaphoreSlim _remoteWorkerSemaphore = new(1, 1);
    private CancellationTokenSource _remoteQueueCts = new();

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _downloadCts = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _uploadCts = new();

    public HistoryViewModel(
        HistoryManager historyManager,
        VirtualKeyboard keyboard,
        [FromKeyedServices(Env.RuntimeConfigName)] ConfigBase runtimeConfig,
        ConfigManager configManager,
        ILogger logger,
        LocalClipboardSetter localClipboardSetter,
        ProfileActionBuilder profileActionBuilder,
        RemoteClipboardServerFactory remoteServerFactory,
        HistorySyncer historySyncer)
    {
        this.historyManager = historyManager;
        this.keyboard = keyboard;
        this.runtimeConfig = runtimeConfig;
        this._configManager = configManager;
        this.logger = logger;
        this.localClipboardSetter = localClipboardSetter;
        this.profileActionBuilder = profileActionBuilder;
        this.remoteServerFactory = remoteServerFactory;
        this.historySyncer = historySyncer;

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
            CancelServerOperations();
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
    private DateTime? _timeCursor = null;
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

    private bool _isLocalEnd = false;
    private bool _isRemoteEnd = false;
    private bool IsEnd => _isLocalEnd && _isRemoteEnd;

    [RelayCommand]
    public Task Refresh()
    {
        CancelServerOperations();
        _isLocalEnd = false;
        _isRemoteEnd = false;

        allHistoryItems.Clear();
        _timeCursor = null;
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
        historyManager.HistoryAdded += record => RecordUpdated(record, false);
        historyManager.HistoryUpdated += record => RecordUpdated(record, true);
        historyManager.HistoryRemoved += record => allHistoryItems.Remove(new HistoryRecordVM(record));

        await Refresh();

        remoteServerFactory.CurrentServerChanged += (sender, e) => OnRemoteServerChanged();
    }

    private void RecordUpdated(HistoryRecord record, bool onlyUpdate)
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

        if (oldRecord != null)
        {
            oldRecord.Update(newRecord);
            return;
        }

        if (onlyUpdate)
        {
            return;
        }
        InsertHistoryInOrder(record);
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
    }

    private async Task LoadRemotePage()
    {
        if (historySyncServer == null)
        {
            return;
        }

        var (types, starred, searchText) = BuildQueryParameters();
        var queryInfo = new HistorySyncInfo
        {
            Types = types,
            Starred = starred,
            SearchText = searchText,
            BeforeDate = _timeCursor
        };
        _remoteFetchQueue.EnqueueTail(queryInfo);
        try
        {
            await ProcessRemoteQueueAsync(waitAllTasks: true);
        }
        catch { }
    }

    private async Task DoLoadPageAsync(int size, CancellationToken token)
    {
        var (types, started, searchText) = BuildQueryParameters();

        if (_isLocalEnd)
        {
            await LoadRemotePage();
            return;
        }

        IsLoadingLocal = true;
        using var guard = new ScopeGuard(() => IsLoadingLocal = false);

        var records = await historyManager.GetHistoryAsync(
            types,
            started,
            _timeCursor,
            size,
            string.IsNullOrEmpty(searchText) ? null : searchText,
            token);

        _isLocalEnd = records.Count == 0;
        if (_isLocalEnd)
        {
            await LoadRemotePage();
            return;
        }

        var vms = records.Select(x => new HistoryRecordVM(x)).ToList();
        allHistoryItems.AddRange(vms);

        var last = vms.LastOrDefault()!;

        var queryInfo = new HistorySyncInfo
        {
            Types = types,
            Starred = started,
            SearchText = searchText,
            BeforeDate = _timeCursor,
            AfterDate = last.Timestamp
        };
        _remoteFetchQueue.EnqueueTail(queryInfo);
        _ = ProcessRemoteQueueAsync(waitAllTasks: false);

        _timeCursor = last.Timestamp;
        _isLocalEnd = vms.Count < size;
    }

    private async Task ProcessRemoteQueueAsync(bool waitAllTasks = false)
    {
        if (historySyncServer == null || _isRemoteEnd)
        {
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
            while (errorCount <= 5 && !ct.IsCancellationRequested && _remoteFetchQueue.TryDequeue(out var queryInfo))
            {
                try
                {
                    await ProcessSingleRemoteSyncAsync(queryInfo, ct);
                }
                catch (Exception ex) when (ct.IsCancellationRequested == false)
                {
                    logger.Write("[HISTORY_VIEW_MODEL] Remote fetch failed:", ex.Message);
                    errorCount++;
                    _remoteFetchQueue.EnqueueHead(queryInfo);
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

    private async Task ProcessSingleRemoteSyncAsync(HistorySyncInfo queryInfo, CancellationToken ct)
    {
        List<HistoryRecord> addedRecords;
        if (_isLocalEnd)
        {
            addedRecords = await historySyncer.SyncRangeAsync(
                queryInfo.BeforeDate,
                queryInfo.Types,
                string.IsNullOrEmpty(queryInfo.SearchText) ? null : queryInfo.SearchText,
                queryInfo.Starred,
                1,
                ct);
            _isRemoteEnd = addedRecords.Count == 0;
            _timeCursor = addedRecords.LastOrDefault()?.Timestamp ?? _timeCursor;
        }
        else
        {
            addedRecords = await historySyncer.SyncRangeAsync(
                queryInfo.BeforeDate,
                queryInfo.AfterDate,
                queryInfo.Types,
                string.IsNullOrEmpty(queryInfo.SearchText) ? null : queryInfo.SearchText,
                queryInfo.Starred,
                ct);
        }
        addedRecords.ForEach(record =>
        {
            RecordUpdated(record, false);
        });
    }

    [RelayCommand]
    private async Task DownloadRemoteProfile(HistoryRecordVM vm)
    {
        if (historySyncServer is null)
        {
            return;
        }

        try
        {
            vm.HasError = false;
            vm.ErrorMessage = string.Empty;

            var record = vm.ToHistoryRecord();
            var profile = record.ToProfile();

            if (profile.NeedsTransferData(out var localDataPath) is false)
            {
                return;
            }

            var profileId = Profile.GetProfileId(record.Type, record.Hash);
            var cts = new CancellationTokenSource();
            if (!_downloadCts.TryAdd(profileId, cts))
            {
                cts.Dispose();
                return;
            }

            vm.IsDownloading = true;
            IProgress<HttpDownloadProgress>? progress = vm.CreateDownloadProgress();

            await historySyncServer.DownloadHistoryDataAsync(profileId, localDataPath, progress, cts.Token);
            await profile.SetTranseferData(localDataPath, true, cts.Token);

            record.SetFilePath(await profile.Persistentize(cts.Token));
            record.IsLocalFileReady = true;

            await historyManager.UpdateHistory(record, cts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Write("[HISTORY_VIEW_MODEL] Download remote profile failed:", ex.Message);
            vm.HasError = true;
            vm.ErrorMessage = ex.Message;
            vm.SyncState = SyncStatus.SyncError;
        }
        finally
        {
            vm.IsDownloading = false;
            vm.DownloadProgress = 0;
            if (_downloadCts.TryRemove(Profile.GetProfileId(vm.Type, vm.Hash), out var cts))
            {
                cts.Dispose();
            }
        }
    }

    public async Task<List<MenuItem>> BuildActionsAsync(HistoryRecordVM record)
    {
        if (record.SyncState == SyncStatus.ServerOnly)
        {
            return
            [
                new MenuItem(I18n.Strings.DeleteHistory, () => { _ = historyManager.DeleteHistory(record.ToHistoryRecord()); }),
                new MenuItem(I18n.Strings.Download, () => _ = DownloadRemoteProfile(record))
            ];
        }

        if (record.SyncState == SyncStatus.LocalOnly && historySyncServer != null)
        {
            return
            [
                new MenuItem(I18n.Strings.DeleteHistory, () => { _ = historyManager.DeleteHistory(record.ToHistoryRecord()); }),
                new MenuItem(I18n.Strings.Uploaded, () => _ = UploadLocalHistoryAsync(record))
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

        return await profileActionBuilder.Build(profile, CancellationToken.None);
    }

    [RelayCommand]
    private async Task UploadLocalHistoryAsync(HistoryRecordVM vm)
    {
        if (historySyncServer == null)
        {
            return;
        }
        try
        {
            var record = vm.ToHistoryRecord();
            var profile = record.ToProfile();
            string? transferFilePath = await profile.PrepareDataWithCache(CancellationToken.None);

            var pid = $"{record.Type}-{record.Hash}";
            var cts = new CancellationTokenSource();
            if (!_uploadCts.TryAdd(pid, cts))
            {
                cts.Dispose();
                return;
            }
            vm.IsUploading = true;
            vm.UploadProgress = 0;
            IProgress<HttpDownloadProgress>? progress = vm.CreateUploadProgress(transferFilePath);

            await historySyncServer.UploadHistoryAsync(record.ToHistoryRecordDto(), transferFilePath, progress, cts.Token);
            vm.SyncState = SyncStatus.Synced;
        }
        catch (RemoteHistoryConflictException ex)
        {
            await HandleUploadConflictAsync(vm, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            vm.SyncState = SyncStatus.SyncError;
            vm.ErrorMessage = ex.Message;
        }
        finally
        {
            vm.IsUploading = false;
            var pid = $"{vm.Type}-{vm.Hash}";
            if (_uploadCts.TryRemove(pid, out var cts))
            {
                cts.Dispose();
            }
        }
    }

    [RelayCommand]
    private void CancelUpload(HistoryRecordVM vm)
    {
        var pid = $"{vm.Type}-{vm.Hash}";
        if (_uploadCts.TryGetValue(pid, out var cts))
        {
            try { cts.Cancel(); } catch { }
        }
    }

    private async Task HandleUploadConflictAsync(HistoryRecordVM vm, RemoteHistoryConflictException ex)
    {
        try
        {
            var record = vm.ToHistoryRecord();
            if (ex.ServerRecord != null)
            {
                record.ApplyFromServerUpdateDto(ex.ServerRecord);
            }
            await historyManager.PersistServerSyncedAsync(record, CancellationToken.None);
        }
        catch (Exception e)
        {
            vm.ErrorMessage = e.Message;
            vm.SyncState = SyncStatus.SyncError;
        }
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

    [RelayCommand]
    private void CancelDownload(HistoryRecordVM vm)
    {
        var pid = $"{vm.Type}-{vm.Hash}";
        if (_downloadCts.TryGetValue(pid, out var cts))
        {
            cts.Cancel();
        }
    }

    private void CancelServerOperations()
    {
        // 取消所有下载
        foreach (var kv in _downloadCts)
        {
            try { kv.Value.Cancel(); } catch { }
        }
        _downloadCts.Clear();

        // 取消所有上传
        foreach (var kv in _uploadCts)
        {
            try { kv.Value.Cancel(); } catch { }
        }
        _uploadCts.Clear();

        // 取消远程同步
        CancelFetchTask();
    }

    internal class HistorySyncInfo
    {
        public ProfileTypeFilter Types { get; set; }
        public bool? Starred { get; set; }
        public string? SearchText { get; set; }
        public DateTime? AfterDate { get; set; }
        public DateTime? BeforeDate { get; set; }
    }
}