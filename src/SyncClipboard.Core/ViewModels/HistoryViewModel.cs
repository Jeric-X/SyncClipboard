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
using SyncClipboard.Core.UserServices;
using SyncClipboard.Core.UserServices.ClipboardService;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.Utilities.Keyboard;
using SyncClipboard.Core.Utilities.Runner;
using SyncClipboard.Core.ViewModels.Sub;
using System.Threading.Channels;
using ElapsedEventArgs = System.Timers.ElapsedEventArgs;
using Timer = System.Timers.Timer;

namespace SyncClipboard.Core.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(30);

    private IWindow window = null!;
    private bool _hasShownWindow;

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
    private readonly ICaretPositionProvider _caretPositionProvider;
    private readonly INativeWindowController _foregroundWindowInfoProvider;
    private readonly IMousePositionProvider _mousePositionProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly ForegroundWindowTrackingService _foregroundWindowTrackingService;
    private IOfficialSyncServer? historySyncServer;

    [ObservableProperty]
    private bool _enableSyncHistory;

    private readonly HistoryTransferQueue _transferQueue;
    private readonly IThreadDispatcher _threadDispatcher;
    private CancellationTokenSource _loadCts = new();
    private TaskCompletionSource<bool>? _transferringLoadingTcs;

    private Channel<TransferTask> _updateTaskQueue = Channel.CreateUnbounded<TransferTask>();
    private Task? _queueConsumerTask;

    private readonly Timer _relativeTimeTimer;

    private async Task RunWithOperationTimeoutAsync(
        string operationName,
        Func<CancellationToken, Task> operation,
        CancellationToken externalToken = default)
    {
        using var cancellation = externalToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(externalToken)
            : new CancellationTokenSource();
        cancellation.CancelAfter(OperationTimeout);

        try
        {
            await operation(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            var reason = externalToken.IsCancellationRequested
                ? "was canceled"
                : $"timed out after {OperationTimeout.TotalSeconds:0} seconds";
            await logger.WriteAsync($"History operation '{operationName}' {reason}.");
        }
    }

    private async Task<T> RunWithOperationTimeoutAsync<T>(
        string operationName,
        Func<CancellationToken, Task<T>> operation,
        T canceledResult,
        CancellationToken externalToken = default)
    {
        using var cancellation = externalToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(externalToken)
            : new CancellationTokenSource();
        cancellation.CancelAfter(OperationTimeout);

        try
        {
            return await operation(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            var reason = externalToken.IsCancellationRequested
                ? "was canceled"
                : $"timed out after {OperationTimeout.TotalSeconds:0} seconds";
            await logger.WriteAsync($"History operation '{operationName}' {reason}.");
            return canceledResult;
        }
    }

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
        IThreadDispatcher threadDispatcher,
        ICaretPositionProvider caretPositionProvider,
        INativeWindowController foregroundWindowInfoProvider,
        IMousePositionProvider mousePositionProvider,
        ForegroundWindowTrackingService foregroundWindowTrackingService,
        IServiceProvider serviceProvider)
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
        this._caretPositionProvider = caretPositionProvider;
        this._foregroundWindowInfoProvider = foregroundWindowInfoProvider;
        this._mousePositionProvider = mousePositionProvider;
        this._foregroundWindowTrackingService = foregroundWindowTrackingService;
        this._serviceProvider = serviceProvider;

        _transferQueue.TaskStatusChanged += OnTransferTaskStatusChanged;

        var currentServer = remoteServerFactory.Current;
        _enableSyncHistory = runtimeConfig.GetConfig<RuntimeHistoryConfig>().EnableSyncHistory;
        historySyncServer = _enableSyncHistory ? currentServer as IOfficialSyncServer : null;

        runtimeConfig.ListenConfig<RuntimeHistoryConfig>(OnHistoryConfigChanged);

        RefreshFilterOptions();
        viewController = allHistoryItems.CreateView(x => x);
        HistoryItems = viewController.ToNotifyCollectionChanged();
        InitializeSelection();
        ApplyFilter();

        _relativeTimeTimer = new Timer(60000); // 1分钟
        _relativeTimeTimer.Elapsed += OnRelativeTimeTimerElapsed;
        _relativeTimeTimer.AutoReset = true;
        _relativeTimeTimer.Start();
    }

    private void OnHistoryConfigChanged(RuntimeHistoryConfig cfg)
    {
        _ = _threadDispatcher.RunOnMainThreadAsync(() =>
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
        });
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

        return true;
    }

    private bool IsStarredScopeActive => OnlyShowStarred || SelectedFilter == HistoryFilterType.Starred;

    [ObservableProperty]
    private HistoryFilterType selectedFilter = HistoryFilterType.All;
    partial void OnSelectedFilterChanged(HistoryFilterType value)
    {
        _ = Reload();
        RequestSelectionSummaryRefresh(SelectionSummaryPart.Counts);
        OnPropertyChanged(nameof(SelectedFilterOption));
    }

    [ObservableProperty]
    private string searchText = string.Empty;
    partial void OnSearchTextChanged(string value)
    {
        _ = Reload();
        RequestSelectionSummaryRefresh(SelectionSummaryPart.Counts);
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

    private static readonly LocaleString<HistoryFilterType> allFilterOption = new(HistoryFilterType.All, I18n.Strings.HistoryFilterAll);
    private static readonly LocaleString<HistoryFilterType> textFilterOption = new(HistoryFilterType.Text, I18n.Strings.HistoryFilterText);
    private static readonly LocaleString<HistoryFilterType> imageFilterOption = new(HistoryFilterType.Image, I18n.Strings.HistoryFilterImage);
    private static readonly LocaleString<HistoryFilterType> fileFilterOption = new(HistoryFilterType.File, I18n.Strings.HistoryFilterFile);
    private static readonly LocaleString<HistoryFilterType> starredFilterOption = new(HistoryFilterType.Starred, I18n.Strings.HistoryFilterStarred);
    private static readonly LocaleString<HistoryFilterType> transferringFilterOption = new(HistoryFilterType.Transferring, I18n.Strings.HistoryFilterTransferring);

    private IReadOnlyList<LocaleString<HistoryFilterType>> filterOptions = [];
    public IReadOnlyList<LocaleString<HistoryFilterType>> FilterOptions => filterOptions;

    private void RefreshFilterOptions()
    {
        List<LocaleString<HistoryFilterType>> options =
        [
            allFilterOption,
            textFilterOption,
            imageFilterOption,
            fileFilterOption
        ];
        if (ShowStarredFilter)
        {
            options.Add(starredFilterOption);
        }
        options.Add(transferringFilterOption);

        filterOptions = options.ToArray();
        OnPropertyChanged(nameof(FilterOptions));
    }

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
            if (value == IsTopmost) return;

            window?.SetTopmost(value);
            runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { IsTopmost = value });
            OnPropertyChanged(nameof(IsTopmost));
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
            OnPropertyChanged(nameof(ShowSyncStateIndicator));
        }
    }

    [ObservableProperty]
    private bool serverConnected = true;

    public bool OnlyShowStarred
    {
        get => runtimeConfig.GetConfig<HistoryWindowConfig>().OnlyShowStarred;
        set
        {
            if (value == OnlyShowStarred) return;

            runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { OnlyShowStarred = value });
            OnPropertyChanged(nameof(OnlyShowStarred));
            _ = Reload();
            RequestSelectionSummaryRefresh(SelectionSummaryPart.Counts);
        }
    }

    public bool ShowStarredFilter
    {
        get => runtimeConfig.GetConfig<HistoryWindowConfig>().ShowStarredFilter;
        set
        {
            if (value == ShowStarredFilter) return;

            runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { ShowStarredFilter = value });
            OnPropertyChanged(nameof(ShowStarredFilter));
            if (!value && SelectedFilter == HistoryFilterType.Starred)
            {
                SelectedFilter = HistoryFilterType.All;
            }
            RefreshFilterOptions();
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

    public int FontScalePercent
    {
        get => runtimeConfig.GetConfig<HistoryWindowConfig>().FontScalePercent;
        set
        {
            var clamped = Math.Clamp(value, 25, 400);
            if (clamped == FontScalePercent) return;
            runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { FontScalePercent = clamped });
            OnPropertyChanged(nameof(FontScalePercent));
            OnPropertyChanged(nameof(ListItemFontSize));
        }
    }

    public bool FollowCaretPosition
    {
        get => runtimeConfig.GetConfig<HistoryWindowConfig>().FollowCaretPosition;
        set => runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { FollowCaretPosition = value });
    }

    public bool FollowForegroundWindowScreen
    {
        get => runtimeConfig.GetConfig<HistoryWindowConfig>().FollowForegroundWindowScreen;
        set => runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { FollowForegroundWindowScreen = value });
    }

    public bool FollowMousePosition
    {
        get => runtimeConfig.GetConfig<HistoryWindowConfig>().FollowMousePosition;
        set => runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { FollowMousePosition = value });
    }

    public bool ShowPreviewPanel
    {
        get => runtimeConfig.GetConfig<HistoryWindowConfig>().ShowPreviewPanel;
        set
        {
            runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { ShowPreviewPanel = value });
            OnPropertyChanged(nameof(ShowPreviewPanel));
            OnPropertyChanged(nameof(IsCompactListMode));
            OnPropertyChanged(nameof(ShowSyncStateIndicator));
        }
    }

    public int ListViewWidth
    {
        get => runtimeConfig.GetConfig<HistoryWindowConfig>().ListViewWidth;
        set
        {
            if (value < 150) value = 150;
            if (value == ListViewWidth) return;
            runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { ListViewWidth = value });
            OnPropertyChanged(nameof(ListViewWidth));
        }
    }

    public bool CompactListWhenPreview
    {
        get => runtimeConfig.GetConfig<HistoryWindowConfig>().CompactListWhenPreview;
        set
        {
            runtimeConfig.SetConfig(runtimeConfig.GetConfig<HistoryWindowConfig>() with { CompactListWhenPreview = value });
            OnPropertyChanged(nameof(CompactListWhenPreview));
            OnPropertyChanged(nameof(IsCompactListMode));
        }
    }

    /// <summary>
    /// 是否处于紧凑列表模式（CompactListWhenPreview 且 ShowPreviewPanel 都为 true）
    /// </summary>
    public bool IsCompactListMode => CompactListWhenPreview && ShowPreviewPanel;

    /// <summary>
    /// 是否显示同步状态指示器（ShowSyncState 为 true 且 ShowPreviewPanel 为 false）
    /// </summary>
    public bool ShowSyncStateIndicator => ShowSyncState && !ShowPreviewPanel;

    public ScreenPosition? GetCaretPosition()
    {
        return _caretPositionProvider.GetCaretPosition();
    }

    public WindowDetail? GetForegroundWindowInfo()
    {
        return _foregroundWindowInfoProvider.GetForegroundWindowDetail();
    }

    public ScreenPosition? GetMousePosition()
    {
        return _mousePositionProvider.GetMousePosition();
    }

    public bool RepositionWindow()
    {
        if (FollowCaretPosition)
        {
            var position = GetCaretPosition();
            if (position != null && window.SetNearCaretPosition(position))
            {
                return true;
            }
        }
        if (FollowForegroundWindowScreen)
        {
            var foregroundInfo = GetForegroundWindowInfo();
            if (foregroundInfo is { Bounds: { } bounds })
            {
                var centerX = bounds.X + (bounds.Width / 2);
                var centerY = bounds.Y + (bounds.Height / 2);
                if (window.SetPositionOnScreen(centerX, centerY))
                {
                    return true;
                }
            }
        }
        if (FollowMousePosition)
        {
            var position = GetMousePosition();
            if (position != null && window.SetNearMousePosition(position))
            {
                return true;
            }
        }
        return false;
    }

    public double ListItemFontSize => FontScalePercent / 100.0 * 12.0;

    [RelayCommand]
    public Task ChangeStarStatus(HistoryRecordVM record)
    {
        var entity = record.ToHistoryRecord();
        entity.Stared = !record.Stared;
        return historyManager.UpdateHistoryProperty(entity);
    }

    [RelayCommand]
    public Task ChangePinStatus(HistoryRecordVM record)
    {
        var entity = record.ToHistoryRecord();
        entity.Pinned = !record.Pinned;
        return historyManager.UpdateHistoryProperty(entity);
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
    public void TogglePreviewPanel()
    {
        ShowPreviewPanel = !ShowPreviewPanel;
    }

    [RelayCommand]
    public void Close()
    {
        window?.Hide();
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
        ClearSelectedItem();
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
        bool? starred = IsStarredScopeActive ? true : null;
        string? searchText = string.IsNullOrEmpty(SearchText) ? null : SearchText;
        var types = SelectedFilter switch
        {
            HistoryFilterType.Text => ProfileTypeFilter.Text,
            HistoryFilterType.Image => ProfileTypeFilter.Image,
            HistoryFilterType.File => ProfileTypeFilter.FileAndGroup,
            _ => ProfileTypeFilter.All,
        };
        return (types, starred, searchText);
    }

    private int GetSelectedFilterOptionIndex()
    {
        for (var i = 0; i < FilterOptions.Count; i++)
        {
            if (FilterOptions[i].Key == SelectedFilter)
            {
                return i;
            }
        }
        return 0;
    }

    public void NavigateToNextFilter()
    {
        var filterCount = FilterOptions.Count;
        if (filterCount == 0) return;

        var currentIndex = GetSelectedFilterOptionIndex();
        var nextIndex = (currentIndex + 1) % filterCount;
        SelectedFilter = FilterOptions[nextIndex].Key;
    }

    public void NavigateToPreviousFilter()
    {
        var filterCount = FilterOptions.Count;
        if (filterCount == 0) return;

        var currentIndex = GetSelectedFilterOptionIndex();
        var prevIndex = (currentIndex - 1 + filterCount) % filterCount;
        SelectedFilter = FilterOptions[prevIndex].Key;
    }

    public void NavigateDown()
    {
        if (IsMultiSelecting)
            return;

        var count = HistoryItemCount;
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
        if (IsMultiSelecting)
            return;

        var count = HistoryItemCount;
        if (count == 0) return;

        if (SelectedIndex > 0)
        {
            SelectedIndex--;
        }
        window?.ScrollToSelectedItem();
    }

    public void NavigateToFirst()
    {
        var count = HistoryItemCount;
        if (count == 0) return;

        SelectedIndex = 0;
        window?.ScrollToSelectedItem();
    }

    public void OnWindowShown()
    {
        if (!OperatingSystem.IsLinux())
        {
            _foregroundWindowTrackingService.SetHistoryWindow(window.GetNativeWindowInfo());
        }
        if (ScrollToTopOnReopen)
        {
            ScrollToTop();
        }
    }

    public void OnBeforeShownWindow()
    {
        CapturePasteTargetBeforeShowingHistory();
    }

    private void CapturePasteTargetBeforeShowingHistory()
    {
        _foregroundWindowTrackingService.CaptureCurrentForegroundWindow();
    }

    public void ShowWithAutoPosition()
    {
        OnBeforeShownWindow();

        var wasVisible = window.IsVisible;
        if (wasVisible)
        {
            RepositionWindow();
        }
        else if (!RepositionWindow() && !_hasShownWindow)
        {
            window.CenterOnScreen(Width, Height);
        }

        window.Show(activate: true);
        window.FocusSearch();
        OnWindowShown();
        _hasShownWindow = true;
    }

    public void SwitchVisible()
    {
        if (window.IsVisible)
        {
            window.Hide();
            return;
        }

        ShowWithAutoPosition();
    }

    public void NavigateToLast()
    {
        var count = HistoryItemCount;
        if (count == 0) return;

        SelectedIndex = count - 1;
        window?.ScrollToSelectedItem();
    }

    public bool HandleKeyPress(
        Key key,
        bool isShiftPressed = false,
        bool isAltPressed = false,
        bool isCtrlPressed = false,
        bool isMetaPressed = false)
    {
        if (IsCloseShortcut(key, isShiftPressed, isAltPressed, isCtrlPressed, isMetaPressed))
        {
            Close();
            return true;
        }

        if (isCtrlPressed && isShiftPressed && !isAltPressed)
        {
            switch (key)
            {
                case Key.S:
                    OnlyShowStarred = !OnlyShowStarred;
                    return true;
                case Key.T:
                    IsTopmost = !IsTopmost;
                    return true;
            }
        }

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
                case Key.S:
                    HandleToggleStarShortcut();
                    return true;
                case Key.D:
                    HandleDeleteShortcut();
                    return true;
                case Key.P:
                    ShowPreviewPanel = !ShowPreviewPanel;
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
                window?.Hide();
                return true;

            default:
                return false;
        }
    }

    private static bool IsCloseShortcut(
        Key key,
        bool isShiftPressed,
        bool isAltPressed,
        bool isCtrlPressed,
        bool isMetaPressed)
    {
        if (key != Key.W || isShiftPressed || isAltPressed)
            return false;

        return OperatingSystem.IsMacOS()
            ? isMetaPressed && !isCtrlPressed
            : isCtrlPressed && !isMetaPressed;
    }

    private async void HandleToggleStarShortcut()
    {
        if (IsMultiSelecting)
            await ConfirmToggleSelectedStarredAsync();
        else
            await ToggleStarForSelectedItemAsync();
    }

    private async Task ToggleStarForSelectedItemAsync()
    {
        var count = HistoryItemCount;
        if (SelectedIndex < 0 || SelectedIndex >= count)
            return;

        var selectedItem = ((IList<HistoryRecordVM>)HistoryItems)[SelectedIndex];
        if (selectedItem == null) return;

        await ChangeStarStatus(selectedItem);
    }

    private async void HandleDeleteShortcut()
    {
        if (IsMultiSelecting)
            await ConfirmDeleteSelectedAsync();
        else
            await DeleteSelectedItemAsync();
    }

    private async Task DeleteSelectedItemAsync()
    {
        var count = HistoryItemCount;
        if (SelectedIndex < 0 || SelectedIndex >= count)
            return;

        var selectedItem = ((IList<HistoryRecordVM>)HistoryItems)[SelectedIndex];
        if (selectedItem == null) return;

        await DeleteItem(selectedItem);
    }

    private async void HandleEnterKey(bool isAltPressed)
    {
        var count = HistoryItemCount;
        if (SelectedIndex < 0 || SelectedIndex >= count)
            return;

        var selectedItem = ((IList<HistoryRecordVM>)HistoryItems)[SelectedIndex];
        if (selectedItem == null) return;

        // Alt键表示不粘贴到剪贴板，只是复制操作
        var paste = !isAltPressed;
        await HandleCopyButtonAsync(selectedItem, paste);
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
        // Both desktop views bind HistoryItems before calling Init. Subscribe here
        // so the controls process collection changes before selection is adjusted.
        HistoryItems.CollectionChanged -= OnHistoryItemsCollectionChanged;
        HistoryItems.CollectionChanged += OnHistoryItemsCollectionChanged;
        if (!OperatingSystem.IsLinux())
        {
            _foregroundWindowTrackingService.SetHistoryWindow(window.GetNativeWindowInfo());
        }
        historyManager.HistoryAdded += RecordEntityUpdated;
        historyManager.HistoryUpdated += RecordEntityUpdated;
        historyManager.HistoryRemoved += OnHistoryRemoved;

        await Reload();

        remoteServerFactory.CurrentServerChanged += OnCurrentServerChanged;
    }

    private async void RecordEntityUpdated(HistoryRecord record)
    {
        var newRecordVM = new HistoryRecordVM(record) { SortByLastAccessed = SortByLastAccessed };
        await _threadDispatcher.RunOnMainThreadAsync(() =>
        {
            InitVMTransferStatus(newRecordVM);
            RecordUpdated(newRecordVM);
            RefreshSelectionSummaryForUpdatedRecord(newRecordVM);
        });
    }

    private void OnHistoryRemoved(HistoryRecord record)
    {
        _threadDispatcher.RunOnMainThreadAsync(() =>
        {
            ApplyHistoryRecordRemoval(record);
        });
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
        ApplySelectionToRecord(newRecord);
        if (SelectedFilter == HistoryFilterType.Transferring)
        {
            UpdateTransferingRecord(newRecord);
            return;
        }

        var oldRecord = allHistoryItems.FirstOrDefault(r => r == newRecord);
        bool isMatchDbFilter = IsMatchDbFilter(newRecord);

        if (oldRecord != null)
        {
            if (IsStarredScopeActive
                && !newRecord.Stared
                && IsMatchDbFilter(newRecord, ignoreStarredFilter: true)
                && IsMatchUiFilter(newRecord))
            {
                oldRecord.Update(newRecord);
                return;
            }

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
            var selectionAfterReplacement = PrepareSelectionForRecordReplacement(oldR);
            ClearVisibleRecordSelection(oldR);
            allHistoryItems.Remove(oldR);
            if (newR != null)
                InsertHistoryInOrder(newR);
            if (selectionAfterReplacement is not null)
                SelectSingleRecord(selectionAfterReplacement);
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

    private bool IsMatchDbFilter(HistoryRecordVM vm, bool ignoreStarredFilter = false)
    {
        return MatchesSelectedFilter(vm)
            && MatchesStarredFilter(vm, ignoreStarredFilter)
            && MatchesSearchFilter(vm)
            && IsWithinLoadedTimeRange(vm);
    }

    private bool MatchesSelectedFilter(HistoryRecordVM vm)
    {
        return SelectedFilter switch
        {
            HistoryFilterType.All => true,
            HistoryFilterType.Text => vm.Type == ProfileType.Text,
            HistoryFilterType.Image => vm.Type == ProfileType.Image,
            HistoryFilterType.File => vm.Type == ProfileType.File || vm.Type == ProfileType.Group,
            _ => true
        };
    }

    private bool MatchesStarredFilter(HistoryRecordVM vm, bool ignoreStarredFilter)
    {
        return ignoreStarredFilter || !IsStarredScopeActive || vm.Stared;
    }

    private bool MatchesSearchFilter(HistoryRecordVM vm)
    {
        return string.IsNullOrEmpty(SearchText)
            || vm.Text.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    // 已加载范围是 >= _timeCursor 的记录，< _timeCursor 的记录还未加载
    private bool IsWithinLoadedTimeRange(HistoryRecordVM vm)
    {
        if (_isLocalEnd || !_timeCursor.HasValue)
        {
            return true;
        }

        var recordTime = SortByLastAccessed ? vm.LastAccessed : vm.Timestamp;
        return recordTime >= _timeCursor.Value;
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

        var (types, starred, searchText) = BuildQueryParameters();

        if (_isLocalEnd)
        {
            return;
        }

        IsLoadingLocal = true;
        using var guard = new ScopeGuard(() => IsLoadingLocal = false);

        var records = await historyManager.GetHistoryAsync(
            types,
            starred,
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

        var sortByLastAccessed = SortByLastAccessed;
        var vms = await Task.Run(() =>
        {
            return records.Select(x =>
            {
                var vm = new HistoryRecordVM(x) { SortByLastAccessed = sortByLastAccessed };
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
            return;

        await RunWithOperationTimeoutAsync("download remote history record", async operationToken =>
        {
            var record = vm.ToHistoryRecord();
            var profile = record.ToProfile();

            if (await profile.IsLocalDataValid(false, operationToken))
            {
                record.IsLocalFileReady = true;
                await historyManager.UpdateHistoryLocalInfo(record, operationToken);
                return;
            }

            _ = await _transferQueue.EnqueueDownload(profile, forceResume: true, ct: operationToken);
        });
    }

    public Task<List<MenuItem>> BuildActionsAsync(HistoryRecordVM record) =>
        RunWithOperationTimeoutAsync(
            "build history record actions",
            token => BuildActionsCoreAsync(record, token),
            []);

    private async Task<List<MenuItem>> BuildActionsCoreAsync(HistoryRecordVM record, CancellationToken token)
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
                    await RunWithOperationTimeoutAsync(
                        "copy history hash",
                        token => localClipboardSetter.Set(profile, token));
                }
                catch { }
            }));
        }

        var historyRecord = record.ToHistoryRecord();
        if (!historyRecord.IsLocalFileReady)
        {
            AddMissingLocalFileActions(actions, historyRecord);
            return actions;
        }

        var profile = historyRecord.ToProfile();
        var valid = await profile.IsLocalDataValid(true, token);

        if (!valid)
        {
            historyRecord.IsLocalFileReady = false;
            await historyManager.UpdateHistoryLocalInfo(historyRecord, token);
            AddMissingLocalFileActions(actions, historyRecord);
        }
        else
        {
            var menuItems = await profileActionBuilder.Build(profile, token);
            actions.AddRange(menuItems);
        }
        return actions;
    }

    private void AddMissingLocalFileActions(List<MenuItem> actions, HistoryRecord record)
    {
        if (record.FilePath.FirstOrDefault() is { } filePath &&
            Path.GetDirectoryName(filePath) is { Length: > 0 } containingFolder)
        {
            actions.Add(new MenuItem(
                I18n.Strings.CopyContainingFolderPath,
                () => _ = RunWithOperationTimeoutAsync(
                    "copy missing history file containing folder",
                    token => localClipboardSetter.Set(new TextProfile(containingFolder), token))));
        }

        actions.Add(new MenuItem(
            I18n.Strings.ReloadLocalFile,
            () => _ = RunWithOperationTimeoutAsync(
                "reload local history file",
                token => ReloadLocalHistoryFileAsync(record, token))));
        actions.Add(new MenuItem(
            I18n.Strings.DeleteHistory,
            () => _ = RunWithOperationTimeoutAsync(
                "delete history record",
                token => historyManager.DeleteHistory(record, token))));
    }

    private async Task ReloadLocalHistoryFileAsync(HistoryRecord record, CancellationToken token)
    {
        var profile = record.ToProfile();
        if (!await profile.IsLocalDataValid(false, token))
        {
            ShowWindowToastInfo(I18n.Strings.UnableToCopyByMissingFile);
            return;
        }

        record.IsLocalFileReady = true;
        await historyManager.UpdateHistoryLocalInfo(record, token);
    }

    [RelayCommand]
    private Task ShowOperationInstructionsAsync()
    {
        var dialog = _serviceProvider.GetRequiredKeyedService<IMainWindowDialog>("HistoryWindow");
        var message = $"{I18n.Strings.HistoryWindowKeyboardShortcuts}\n{I18n.Strings.HistoryWindowKeyboardList}\n\n" +
            $"{I18n.Strings.HistoryWindowMouseOperations}\n{I18n.Strings.HistoryWindowMouseList}";
        return dialog.ShowMessageAsync(I18n.Strings.HistoryWindowOperations, message);
    }

    [RelayCommand]
    private async Task UploadLocalHistoryAsync(HistoryRecordVM vm)
    {
        if (historySyncServer == null)
            return;

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
                return;
        }

        _ = await _transferQueue.EnqueueUpload(profile, forceResume: true, ct: CancellationToken.None);
    }

    [RelayCommand]
    private void CancelUpload(HistoryRecordVM vm)
    {
        var profileId = Profile.GetProfileId(vm.Type, vm.Hash);
        _transferQueue.CancelUpload(profileId);
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
            window.Hide();
        }
    }

    private void UpdateAllRelativeTimes()
    {
        _threadDispatcher.RunOnMainThreadAsync(() =>
        {
            foreach (var item in allHistoryItems)
            {
                item.UpdateRelativeTime();
            }
        });
    }

    private void OnRelativeTimeTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        UpdateAllRelativeTimes();
    }

    /// <summary>
    /// Applies the platform-independent effects of clicking a history record.
    /// Returns whether the view should mark the pointer event as handled.
    /// </summary>
    public bool HandleItemClick(
        HistoryRecordVM record,
        bool ctrlPressed,
        bool shiftPressed,
        bool primaryButtonPressed,
        bool middleButtonPressed,
        bool rightButtonPressed)
    {
        if (rightButtonPressed)
            return IsMultiSelecting;

        if (!primaryButtonPressed && !middleButtonPressed)
            return false;

        if (primaryButtonPressed)
            HandleRecordClick(record, ctrlPressed, shiftPressed);
        SelectSingleRecord(record);

        if (middleButtonPressed)
            _ = HandleCopyButtonAsync(record, true);

        return middleButtonPressed
            || (primaryButtonPressed
                && (IsMultiSelecting || ctrlPressed || shiftPressed));
    }

    public void HandleSelectionCheckBoxClick(HistoryRecordVM record)
    {
        ToggleRecordSelection(record);
        SelectSingleRecord(record);
    }

    public void HandleItemDoubleClick(HistoryRecordVM record)
    {
        if (IsMultiSelecting)
            return;

        _ = HandleCopyButtonAsync(record, false);
    }

    public void HandleImageDoubleClick(HistoryRecordVM record)
    {
        ViewImage(record);
    }

    public Task<bool> FillDragPackage(object package, HistoryRecordVM record) =>
        RunWithOperationTimeoutAsync(
            "fill history drag package",
            token => FillDragPackageCoreAsync(package, record, token),
            false);

    private async Task<bool> FillDragPackageCoreAsync(
        object package,
        HistoryRecordVM record,
        CancellationToken token)
    {
        var historyRecord = record.ToHistoryRecord();
        var profile = historyRecord.ToProfile();
        var valid = await profile.IsLocalDataValid(true, token);
        if (!valid)
        {
            historyRecord.IsLocalFileReady = false;
            await historyManager.UpdateHistoryLocalInfo(historyRecord, token);
            ShowWindowToastInfo(I18n.Strings.UnableToCopyByMissingFile);
            return false;
        }

        var profileType = profile.GetType();
        var setterInterface = typeof(IClipboardSetter<>).MakeGenericType(profileType);

        if (_serviceProvider.GetService(setterInterface) is not IClipboardSetter setter)
        {
            await logger.WriteAsync($"No IClipboardSetter service is registered for type {profileType.Name}");
            return false;
        }

        try
        {
            var localInfo = await profile.Localize(profileEnv.GetPersistentDir(), false, token);
            await setter.FillPackage(package, localInfo.GetMetaInfomation());
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await logger.WriteAsync($"Failed to fill drag package: {ex.Message}");
            return false;
        }
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

            vm = new HistoryRecordVM(record) { SortByLastAccessed = SortByLastAccessed };
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
