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
using SyncClipboard.Core.UserServices.ClipboardService;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.Utilities.Keyboard;
using SyncClipboard.Core.Utilities.Runner;
using SyncClipboard.Core.ViewModels.Sub;
using System.Threading.Channels;

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
    private readonly IProfileEnv profileEnv;
    private readonly HistoryService _historyService;
    private IOfficialSyncServer? historySyncServer;

    [ObservableProperty]
    private bool _enableSyncHistory;

    private readonly HistoryTransferQueue _transferQueue;
    private readonly IThreadDispatcher _threadDispatcher;
    private CancellationTokenSource _loadCts = new();
    private TaskCompletionSource<bool>? _transferringLoadingTcs;

    private Channel<TransferTask> _updateTaskQueue = Channel.CreateUnbounded<TransferTask>();
    private Task? _queueConsumerTask;

    public HistoryViewModel(
        HistoryManager historyManager,
        VirtualKeyboard keyboard,
        [FromKeyedServices(Env.RuntimeConfigName)] ConfigBase runtimeConfig,
        ConfigManager configManager,
        ILogger logger,
        LocalClipboardSetter localClipboardSetter,
        ProfileActionBuilder profileActionBuilder,
        RemoteClipboardServerFactory remoteServerFactory,
        IProfileEnv profileEnv,
        HistorySyncer historySyncer,
        HistoryService historyService,
        HistoryTransferQueue transferQueue,
        IThreadDispatcher threadDispatcher)
    {
        this.historyManager = historyManager;
        this.keyboard = keyboard;
        this.runtimeConfig = runtimeConfig;
        this._configManager = configManager;
        this.logger = logger;
        this.localClipboardSetter = localClipboardSetter;
        this.profileActionBuilder = profileActionBuilder;
        this.remoteServerFactory = remoteServerFactory;
        this.profileEnv = profileEnv;
        this.historySyncer = historySyncer;
        this._historyService = historyService;
        this._transferQueue = transferQueue;
        this._threadDispatcher = threadDispatcher;

        _transferQueue.TaskStatusChanged += OnTransferTaskStatusChanged;

        var currentServer = remoteServerFactory.Current;
        _enableSyncHistory = runtimeConfig.GetConfig<RuntimeHistoryConfig>().EnableSyncHistory;
        historySyncServer = _enableSyncHistory ? currentServer as IOfficialSyncServer : null;

        runtimeConfig.ListenConfig<RuntimeHistoryConfig>(OnHistoryConfigChanged);

        viewController = allHistoryItems.CreateView(x => x);
        HistoryItems = viewController.ToNotifyCollectionChanged();
        ApplyFilter();
    }

    private void OnHistoryConfigChanged(RuntimeHistoryConfig cfg)
    {
        var newEnable = cfg.EnableSyncHistory;
        if (newEnable == EnableSyncHistory)
            return;

        EnableSyncHistory = newEnable;
        if (EnableSyncHistory)
        {
            historySyncServer = remoteServerFactory.Current as IOfficialSyncServer;
            _ = Reload();
        }
        else
        {
            historySyncServer = null;
        }
    }

    private void ApplyFilter()
    {
        viewController.AttachFilter(IsMatchUiFilter);
        window?.ScrollToTop();
    }

    private bool IsMatchUiFilter(HistoryRecordVM record)
    {
        if (SelectedFilter == HistoryFilterType.Transferring)
        {
            return record.IsDownloading || record.IsUploading;
        }

        if (OnlyShowLocal && record.SyncState == SyncStatus.ServerOnly)
            return false;

        if (SelectedFilter == HistoryFilterType.Starred)
        {
            return record.Stared;
        }
        return true;
    }

    [ObservableProperty]
    private int selectedIndex = -1;

    [ObservableProperty]
    private HistoryFilterType selectedFilter = HistoryFilterType.All;
    partial void OnSelectedFilterChanged(HistoryFilterType value)
    {
        _ = Reload();
        OnPropertyChanged(nameof(SelectedFilterOption));
    }

    [ObservableProperty]
    private string searchText = string.Empty;
    partial void OnSearchTextChanged(string value)
    {
        _ = Reload();
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
            new(HistoryFilterType.Starred, I18n.Strings.HistoryFilterStarred),
            new(HistoryFilterType.Transferring, I18n.Strings.HistoryFilterTransferring)
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
            if (value == OnlyShowLocal) return;

            runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { OnlyShowLocal = value });
            OnPropertyChanged(nameof(OnlyShowLocal));
            _ = Reload();
        }
    }

    public bool SortByLastAccessed
    {
        get => runtimeConfig.GetConfig<HistoryWindowConfig>().SortByLastAccessed;
        set
        {
            if (value == SortByLastAccessed) return;

            runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { SortByLastAccessed = value });
            OnPropertyChanged(nameof(SortByLastAccessed));
            _ = Reload();
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
        return historyManager.UpdateHistoryProperty(record.ToHistoryRecord());
    }

    [RelayCommand]
    public Task ChangePinStatus(HistoryRecordVM record)
    {
        record.Pinned = !record.Pinned;
        return historyManager.UpdateHistoryProperty(record.ToHistoryRecord());
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
        if (SelectedFilter == HistoryFilterType.Transferring) return;

        await RunLoadTask(MorePageSize, _loadCts.Token);
    }

    private async Task RunLoadTask(int size, CancellationToken token)
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
                await logger.WriteAsync("Failed to load more history:", ex.Message);
            }
        }, token);
    }

    public bool IsLoading => IsLoadingLocal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    private bool isLoadingLocal = false;

    private bool _isLocalEnd = false;
    private bool IsEnd => _isLocalEnd;

    [RelayCommand]
    public async Task Refresh()
    {
        await Reload();
        _ = _historyService.SyncAllAsync();
    }

    private Task Reload()
    {
        try
        {
            _loadCts.Cancel();
            _loadCts.Dispose();
        }
        catch { }
        _loadCts = new CancellationTokenSource();

        _isLocalEnd = false;

        _transferringLoadingTcs = null;
        allHistoryItems.Clear();
        _timeCursor = null;
        _lastViewportHeight = 0;
        _lastExtentHeight = 0;
        _updateTaskQueue = Channel.CreateUnbounded<TransferTask>();
        _queueConsumerTask = Task.Run(() => _ = ConsumeUpdateTaskQueueAsync(_loadCts.Token));
        window?.ScrollToTop();

        if (SelectedFilter == HistoryFilterType.Transferring)
        {
            return LoadTransferringTasksAsync(_loadCts.Token);
        }

        return RunLoadTask(InitialPageSize, _loadCts.Token);
    }

    private async Task LoadTransferringTasksAsync(CancellationToken token)
    {
        IsLoadingLocal = true;
        try
        {
            _transferQueue.TaskStatusChanged -= OnTransferTaskStatusChanged;
            _transferringLoadingTcs = new TaskCompletionSource<bool>();

            await _transferQueue.ActiveTaskAddMutex.WaitAsync(token);
            _transferQueue.TaskStatusChanged += OnTransferTaskStatusChanged;
            var tasks = await _transferQueue.GetAllActiveTasks(token);
            _transferQueue.ActiveTaskAddMutex.Release();

            foreach (var task in tasks)
            {
                OnTransferTaskStatusChanged(null, task);
            }
            _isLocalEnd = true;
        }
        catch (Exception ex)
        {
            await logger.WriteAsync("Failed to load transferring tasks:", ex.Message);
        }
        finally
        {
            IsLoadingLocal = false;
            _transferringLoadingTcs?.TrySetResult(true);
        }
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
                types = ProfileTypeFilter.FileAndGroup;
                break;
            case HistoryFilterType.Starred:
                types = ProfileTypeFilter.All;
                starred = true;
                break;
            case HistoryFilterType.Transferring:
                types = ProfileTypeFilter.All;
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
        historyManager.HistoryAdded += RecordEntityUpdated;
        historyManager.HistoryUpdated += RecordEntityUpdated;
        historyManager.HistoryRemoved += OnHistoryRemoved;

        await Reload();

        remoteServerFactory.CurrentServerChanged += OnCurrentServerChanged;
    }

    private async void RecordEntityUpdated(HistoryRecord record)
    {
        var newRecordVM = new HistoryRecordVM(record);
        await _threadDispatcher.RunOnMainThreadAsync(() =>
        {
            InitVMTransferStatus(newRecordVM);
            RecordUpdated(newRecordVM);
        });
    }

    private void OnHistoryRemoved(HistoryRecord record)
    {
        _threadDispatcher.RunOnMainThreadAsync(() => allHistoryItems.Remove(new HistoryRecordVM(record)));
    }

    private void OnCurrentServerChanged(object? sender, EventArgs e)
    {
        _threadDispatcher.RunOnMainThreadAsync(() => OnRemoteServerChanged());
    }

    private void UpdateTransferingRecord(HistoryRecordVM newRecord)
    {
        var oldRecord = allHistoryItems.FirstOrDefault(r => r == newRecord);
        if (oldRecord != null)
        {
            oldRecord.Update(newRecord);
            return;
        }
    }

    private void RecordUpdated(HistoryRecordVM newRecord)
    {
        if (SelectedFilter == HistoryFilterType.Transferring)
        {
            UpdateTransferingRecord(newRecord);
            return;
        }

        var oldRecord = allHistoryItems.FirstOrDefault(r => r == newRecord);
        bool isMatchDbFilter = IsMatchDbFilter(newRecord);

        if (oldRecord != null)
        {
            if (!isMatchDbFilter)
            {
                RemoveInsert(oldRecord, null);
                return;
            }
            bool isShownInUI = IsMatchUiFilter(newRecord);
            bool oldisShownInUI = IsMatchUiFilter(oldRecord);
            if (oldisShownInUI != isShownInUI)
            {
                RemoveInsert(oldRecord, newRecord);
                return;
            }

            // 检查记录的排序位置是否改变
            var currentIndex = allHistoryItems.IndexOf(oldRecord);
            if (currentIndex >= 0 && ShouldChangePosition(newRecord, currentIndex))
            {
                // 位置改变，先删除后重新插入
                RemoveInsert(oldRecord, newRecord);
                return;
            }

            oldRecord.Update(newRecord);
            return;
        }

        if (!isMatchDbFilter)
        {
            return;
        }
        InsertHistoryInOrder(newRecord);
    }

    private void RemoveInsert(HistoryRecordVM oldR, HistoryRecordVM? newR)
    {
        try
        {
            allHistoryItems.Remove(oldR);
            if (newR != null)
                InsertHistoryInOrder(newR);
        }
        catch
        {
            Reload();
        }
    }

    /// <summary>
    /// 检查记录在排序列表中的位置是否应该改变
    /// </summary>
    private bool ShouldChangePosition(HistoryRecordVM vm, int currentIndex)
    {
        if (allHistoryItems.Count == 0) return false;

        var t = SortByLastAccessed ? vm.LastAccessed : vm.Timestamp;

        // 检查与前一个记录的顺序
        if (currentIndex > 0)
        {
            var prevT = SortByLastAccessed ? allHistoryItems[currentIndex - 1].LastAccessed : allHistoryItems[currentIndex - 1].Timestamp;
            if (prevT < t)
                return true; // 应该往前移
        }

        // 检查与后一个记录的顺序
        if (currentIndex < allHistoryItems.Count - 1)
        {
            var nextT = SortByLastAccessed ? allHistoryItems[currentIndex + 1].LastAccessed : allHistoryItems[currentIndex + 1].Timestamp;
            if (nextT > t)
                return true; // 应该往后移
        }

        return false;
    }

    private bool IsMatchDbFilter(HistoryRecordVM vm)
    {
        bool filterMatch = SelectedFilter switch
        {
            HistoryFilterType.All => true,
            HistoryFilterType.Text => vm.Type == ProfileType.Text,
            HistoryFilterType.Image => vm.Type == ProfileType.Image,
            HistoryFilterType.File => vm.Type == ProfileType.File || vm.Type == ProfileType.Group,
            _ => true
        };

        if (!filterMatch)
            return false;

        if (!string.IsNullOrEmpty(SearchText))
        {
            if (!vm.Text.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // 检查时间游标：只显示已加载范围内的记录
        // 已加载范围是 >= _timeCursor 的记录，< _timeCursor 的记录还未加载
        if (!_isLocalEnd && _timeCursor.HasValue)
        {
            var recordTime = SortByLastAccessed ? vm.LastAccessed : vm.Timestamp;
            if (recordTime < _timeCursor.Value)
            {
                return false;
            }
        }

        return true;
    }

    private void InitVMTransferStatus(HistoryRecordVM vm)
    {
        var profileId = Profile.GetProfileId(vm.Type, vm.Hash);
        var task = _transferQueue.GetTaskByProfileId(profileId);

        if (task != null)
        {
            vm.UpdateFromTask(task);
        }
    }

    // 对有序队列实行二分查找
    private void InsertHistoryInOrder(HistoryRecordVM vm)
    {
        if (allHistoryItems.Count == 0)
        {
            allHistoryItems.Insert(0, vm);
            return;
        }

        int low = 0, high = allHistoryItems.Count;
        // 根据 SortByLastAccessed 汽选择排序採用的时间字段
        var t = SortByLastAccessed ? vm.LastAccessed : vm.Timestamp;

        while (low < high)
        {
            int mid = (low + high) >> 1;
            var midT = SortByLastAccessed ? allHistoryItems[mid].LastAccessed : allHistoryItems[mid].Timestamp;
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
        historySyncServer = EnableSyncHistory ? currentServer as IOfficialSyncServer : null;
        _ = Reload();
    }

    private async Task DoLoadPageAsync(int size, CancellationToken token)
    {
        if (SelectedFilter == HistoryFilterType.Transferring)
        {
            return;
        }

        var (types, started, searchText) = BuildQueryParameters();

        if (_isLocalEnd)
        {
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
            SortByLastAccessed,
            token);

        _isLocalEnd = records.Count == 0;
        if (_isLocalEnd)
        {
            return;
        }

        var vms = await Task.Run(() =>
        {
            return records.Select(x =>
            {
                var vm = new HistoryRecordVM(x);
                InitVMTransferStatus(vm);
                return vm;
            }).ToArray();
        }, token);

        var last = vms.LastOrDefault()!;
        _timeCursor = SortByLastAccessed ? last.LastAccessed : last.Timestamp;
        vms.ForEach(RecordUpdated);
        _isLocalEnd = vms.Length < size;
    }

    [RelayCommand]
    private async Task DownloadRemoteProfile(HistoryRecordVM vm)
    {
        if (historySyncServer is null)
        {
            return;
        }

        var record = vm.ToHistoryRecord();
        var profile = record.ToProfile();

        if (await profile.IsLocalDataValid(false, CancellationToken.None))
        {
            record.IsLocalFileReady = true;
            await historyManager.UpdateHistoryLocalInfo(record);
            return;
        }

        _ = await _transferQueue.EnqueueDownload(profile, forceResume: true, ct: CancellationToken.None);
    }

    public async Task<List<MenuItem>> BuildActionsAsync(HistoryRecordVM record)
    {
        var actions = new List<MenuItem>();
        var isDiagnoseMode = _configManager.GetConfig<ProgramConfig>().DiagnoseMode;
        if (isDiagnoseMode)
        {
            actions.Add(new MenuItem($"Hash: {record.Hash}", async () =>
            {
                try
                {
                    var profile = new TextProfile(record.Hash);
                    await localClipboardSetter.Set(profile, CancellationToken.None);
                }
                catch { }
            }));
        }

        var profile = record.ToHistoryRecord().ToProfile();
        var valid = await profile.IsLocalDataValid(true, CancellationToken.None);

        if (!valid)
        {
            var historyRecord = record.ToHistoryRecord();
            historyRecord.IsLocalFileReady = false;
            await historyManager.UpdateHistoryLocalInfo(historyRecord);
            actions.Add(new MenuItem(I18n.Strings.DeleteHistory, () => { _ = historyManager.DeleteHistory(historyRecord); }));
        }
        else
        {
            var menuItems = await profileActionBuilder.Build(profile, CancellationToken.None);
            actions.AddRange(menuItems);
        }
        return actions;
    }

    [RelayCommand]
    private async Task UploadLocalHistoryAsync(HistoryRecordVM vm)
    {
        if (historySyncServer == null)
        {
            return;
        }

        var record = vm.ToHistoryRecord();
        var profile = record.ToProfile();
        var valid = await profile.IsLocalDataValid(false, CancellationToken.None);
        if (!valid)
        {
            ShowWindowToastInfo("Local file is missing or changed, this record will be removed.");
            record.IsLocalFileReady = false;
            await historyManager.UpdateHistoryLocalInfo(record);
            return;
        }

        var validationError = await ContentControlHelper.IsContentValid(profile, CancellationToken.None);
        if (validationError != null)
        {
            var dialog = AppCore.Current.Services.GetRequiredKeyedService<IMainWindowDialog>("HistoryWindow");
            var confirmed = await dialog.ShowConfirmationAsync(
                I18n.Strings.UploadWarning,
                $"{validationError}\n\n{I18n.Strings.ContinueUpload}");
            if (!confirmed)
            {
                return;
            }
        }

        _ = await _transferQueue.EnqueueUpload(profile, forceResume: true, ct: CancellationToken.None);
    }

    [RelayCommand]
    private void CancelUpload(HistoryRecordVM vm)
    {
        var profileId = Profile.GetProfileId(vm.Type, vm.Hash);
        _transferQueue.CancelUpload(profileId);
    }

    public async Task CopyToClipboard(HistoryRecordVM record, bool paste, CancellationToken token)
    {
        var historyRecord = record.ToHistoryRecord();
        var profile = historyRecord.ToProfile();
        var valid = await profile.IsLocalDataValid(true, token);
        if (!valid)
        {
            historyRecord.IsLocalFileReady = false;
            await historyManager.UpdateHistoryLocalInfo(historyRecord, token);

            ShowWindowToastInfo(I18n.Strings.UnableToCopyByMissingFile);
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

    private void ShowWindowToastInfo(string message)
    {
        InfoBarMessage = message;
        ShowInfoBar = true;

        infoBarCancellationSource?.Cancel();
        infoBarCancellationSource = new CancellationTokenSource();
        _ = HideInfoBarAfterDelayAsync(infoBarCancellationSource.Token);
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
        var profileId = Profile.GetProfileId(vm.Type, vm.Hash);
        _transferQueue.CancelDownload(profileId);
    }

    private static Task UICoolDown(CancellationToken token)
    {
        if (OperatingSystem.IsWindows())
        {
            return Task.Delay(10, token);
        }
        return Task.Delay(20, token);
    }
    private async Task UpdateRecordFromTaskInTransferFilter(TransferTask task, CancellationToken token)
    {
        try
        {
            if (_transferringLoadingTcs is null)
                return;
            await _transferringLoadingTcs.Task.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var vm = allHistoryItems.FirstOrDefault(r => Profile.GetProfileId(r.Type, r.Hash) == task.ProfileId);

        if (vm is null)
        {
            var record = await HistoryManager.ToRemoteHistoryRecord(task.Profile, token);

            vm = new HistoryRecordVM(record);
            vm.UpdateFromTask(task);
            if (IsMatchUiFilter(vm))
            {
                allHistoryItems.Add(vm);
                _ = Task.Run(async () =>
                {
                    var record = await historyManager.GetHistoryRecord(await task.Profile.GetHash(token), task.Profile.Type, token);
                    if (record is not null)
                        RecordEntityUpdated(record);
                }, token);
                await UICoolDown(token);
            }
        }
        else
        {
            var newVm = vm.DeepCopy();
            newVm.UpdateFromTask(task);
            var isShownInUI = IsMatchUiFilter(newVm);
            if (!isShownInUI)
            {
                RemoveInsert(vm, null);
            }
            else
            {
                vm.UpdateFromTask(task);
            }
            await UICoolDown(token);
        }
    }

    private void UpdateRecordFromTaskInNormalFilter(TransferTask task)
    {
        var vm = allHistoryItems.FirstOrDefault(r => Profile.GetProfileId(r.Type, r.Hash) == task.ProfileId);
        if (vm == null)
        {
            return;
        }

        var newVm = vm.DeepCopy();
        newVm.UpdateFromTask(task);

        var wasShownInUI = IsMatchUiFilter(vm);
        var isShownInUI = IsMatchUiFilter(newVm);
        if (wasShownInUI != isShownInUI)
        {
            RemoveInsert(vm, newVm);
            return;
        }

        vm.UpdateFromTask(task);
    }

    private void OnTransferTaskStatusChanged(object? sender, TransferTask task)
    {
        _updateTaskQueue.Writer.TryWrite(task);
    }

    private async Task ConsumeUpdateTaskQueueAsync(CancellationToken token)
    {
        try
        {
            await foreach (var task in _updateTaskQueue.Reader.ReadAllAsync(token))
            {
                await _threadDispatcher.RunOnMainThreadAsync(async () =>
                {
                    if (SelectedFilter == HistoryFilterType.Transferring)
                    {
                        await UpdateRecordFromTaskInTransferFilter(task, token);
                    }
                    else
                    {
                        UpdateRecordFromTaskInNormalFilter(task);
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
