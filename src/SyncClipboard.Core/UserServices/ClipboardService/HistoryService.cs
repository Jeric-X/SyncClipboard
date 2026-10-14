using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Core.Utilities.Runner;
using SyncClipboard.Core.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace SyncClipboard.Core.UserServices.ClipboardService;

public class HistoryService : ClipboardHander
{
    private IOfficialSyncServer? _historySyncServer;
    private IRemoteClipboardServer? _currentServer;
    private readonly SingletonTask _syncingTask;
    private DateTimeOffset? _lastSyncTime;
    private readonly HistoryManager historyManager;
    private readonly ConfigManager configManager;
    private readonly ConfigBase runTimeConfig;
    private readonly RemoteClipboardServerFactory remoteServerFactory;
    private readonly HistorySyncer historySyncer;
    private readonly HistorySyncSession syncSession;
    private Action<HistoryRecordDto>? remoteHistoryHandler;
    private readonly ITrayIcon trayIcon;
    private readonly HistoryTransferQueue historyTransferQueue;
    private HistoryConfig _historyConfig;
    private readonly Lock _syncStateLock = new();
    private bool _stopping;
    protected override bool EnableToggleMenuItem => false;

    public HistoryService(
        [FromKeyedServices(Env.RuntimeConfigName)] ConfigBase runtimeConfig,
        HistoryManager historyManager,
        ConfigManager configManager,
        RemoteClipboardServerFactory remoteServerFactory,
        HistorySyncer historySyncer,
        ITrayIcon trayIcon,
        HistoryTransferQueue historyTransferQueue,
        HistorySyncSession syncSession)
    {
        this.runTimeConfig = runtimeConfig;
        this.historyManager = historyManager;
        this.configManager = configManager;
        this.remoteServerFactory = remoteServerFactory;
        this.historySyncer = historySyncer;
        this.syncSession = syncSession;
        this.trayIcon = trayIcon;
        this.historyTransferQueue = historyTransferQueue;
        _historyConfig = configManager.GetConfig<HistoryConfig>();
        configManager.ListenConfig<HistoryConfig>(OnHistoryConfigChanged);
        _syncingTask = new SingletonTask(SyncTaskImpl);
    }

    public override string SERVICE_NAME => I18n.Strings.ClipboardHistory;

    public override string LOG_TAG => "History";
    protected override bool SwitchOn
    {
        get => configManager.GetConfig<HistoryConfig>().EnableHistory;
        set => configManager.SetConfig(configManager.GetConfig<HistoryConfig>() with { EnableHistory = value });
    }

    protected override void StartService()
    {
        lock (_syncStateLock)
        {
            _stopping = false;
        }
        syncSession.Start();
        base.StartService();
        remoteServerFactory.CurrentServerChanged += OnServerChanged;
        OnServerChanged(null, EventArgs.Empty);
    }

    protected override async Task StopSerivceAsync()
    {
        Task syncingStopped;
        lock (_syncStateLock)
        {
            _stopping = true;
            syncingStopped = _syncingTask.CancelAndWaitAsync();
        }
        syncSession.Stop();
        await base.StopSerivceAsync().ConfigureAwait(false);
        remoteServerFactory.CurrentServerChanged -= OnServerChanged;
        UnsubscribeFromServer();
        await syncingStopped.ConfigureAwait(false);
    }

    private void UnsubscribeFromServer()
    {
        if (remoteHistoryHandler is not null)
            _historySyncServer?.HistoryChanged -= remoteHistoryHandler;
        remoteHistoryHandler = null;
        _historySyncServer = null;
        _currentServer?.PollStatusEvent -= OnPollStatusChanged;
        _currentServer = null;
    }

    private void OnServerChanged(object? sender, EventArgs e)
    {
        _syncingTask.Cancel();
        UnsubscribeFromServer();

        var currentServer = remoteServerFactory.Current;
        _currentServer = currentServer;
        SetRuntimeConfig();

        _historySyncServer = currentServer as IOfficialSyncServer;
        if (_historySyncServer is not null)
        {
            var session = syncSession.Capture();
            remoteHistoryHandler = record => OnRemoteHistoryChanged(record, session);
            _historySyncServer.HistoryChanged += remoteHistoryHandler;
            _currentServer.PollStatusEvent += OnPollStatusChanged;
        }

        _lastSyncTime = null;
        if (!remoteServerFactory.HasActiveServer)
        {
            trayIcon.SetStatusString(SERVICE_NAME, "Syncing Disabled.", false);
        }
        TriggerSyncTask();
    }

    private void SetRuntimeConfig()
    {
        var runtimeHistoryConfig = new RuntimeHistoryConfig
        {
            EnableSyncHistory = _currentServer is IOfficialSyncServer
                && _historyConfig.EnableSyncHistory
                && _historyConfig.EnableHistory,
        };

        runTimeConfig.SetConfig(runtimeHistoryConfig);
    }

    private void OnPollStatusChanged(object? sender, PollStatusEventArgs e)
    {
        // 当服务器重连成功时同步所有历史记录
        if (e.Status == PollStatus.Resumed)
        {
            TriggerSyncTask();
        }
        if (e.Status == PollStatus.StoppedDueToNetworkIssues && runTimeConfig.GetConfig<RuntimeHistoryConfig>().EnableSyncHistory)
        {
            trayIcon.SetStatusString(SERVICE_NAME, $"History synchronization failed with error: {e.Message}", error: true);
            _syncingTask.Cancel();
        }
    }

    private async void TriggerSyncTask()
    {
        Task syncingTask;
        lock (_syncStateLock)
        {
            if (_stopping)
            {
                return;
            }

            syncingTask = _syncingTask.Run();
        }

        try
        {
            await syncingTask;
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                return;
            }
            trayIcon.SetStatusString(SERVICE_NAME, $"History synchronization failed with error: {ex.Message}", error: true);
            Logger?.Write($"[{LOG_TAG}] 同步所有历史记录失败: {ex.Message}");
        }
    }

    private void OnHistoryConfigChanged(HistoryConfig cfg)
    {
        if (_historyConfig == cfg)
        {
            return;
        }

        _historyConfig = cfg;
        SetRuntimeConfig();
        TriggerSyncTask();
    }

    public Task SyncAllAsync()
    {
        _lastSyncTime = null;
        TriggerSyncTask();
        return Task.CompletedTask;
    }

    private async Task SyncTaskImpl(CancellationToken token)
    {
        if (runTimeConfig.GetConfig<RuntimeHistoryConfig>().EnableSyncHistory is false || _historySyncServer is null)
        {
            _lastSyncTime = null;
            trayIcon.SetStatusString(SERVICE_NAME, "Organizing local history records...", false);
            await historySyncer.RemoveRemoteHistorys(token).ConfigureAwait(false);
            trayIcon.SetStatusString(SERVICE_NAME, "Syncing Disabled.", false);
            return;
        }

        var session = syncSession.Capture();
        using var request = session.Link(token);
        token = request?.Token ?? session.AutomaticToken;
        var serverTime = await session.Server!.GetServerTimeAsync(token).ConfigureAwait(false);
        session.ThrowIfCancellationRequested();
        token.ThrowIfCancellationRequested();
        var timeDifference = (DateTimeOffset.Now - serverTime).Duration().TotalMinutes;
        if (timeDifference > 5)
        {
            throw new InvalidOperationException(I18n.Strings.ServerTimeError);
        }

        historyManager.EnableCleanup = false;
        using (new ScopeGuard(() => historyManager.EnableCleanup = true))
        {
            trayIcon.SetStatusString(SERVICE_NAME, "Synchronizing history...");
            historyTransferQueue.ResumeQueue();
            await historySyncer.SyncAllAsync(_lastSyncTime?.LocalDateTime, token).ConfigureAwait(false);
        }

        while (!token.IsCancellationRequested)
        {
            await SaveServerTime(session, token).ConfigureAwait(false);
            trayIcon.SetStatusString(SERVICE_NAME, $"Synchronized. Last sync time {_lastSyncTime?.LocalDateTime.ToString("HH:mm")}", false);
            await Task.Delay(TimeSpan.FromMinutes(1), token).ConfigureAwait(false);
        }
    }

    private async Task SaveServerTime(HistorySyncSession.Context session, CancellationToken token)
    {
        try
        {
            var serverTime = await session.Server!.GetServerTimeAsync(token).ConfigureAwait(false);
            session.ThrowIfCancellationRequested();
            token.ThrowIfCancellationRequested();
            _lastSyncTime = serverTime;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { }
    }

    private async void OnRemoteHistoryChanged(HistoryRecordDto historyRecordDto, HistorySyncSession.Context account)
    {
        if (runTimeConfig.GetConfig<RuntimeHistoryConfig>().EnableSyncHistory is false)
        {
            return;
        }

        try
        {
            account.ThrowIfCancellationRequested(automatic: false);
            var session = syncSession.Capture();
            if (session.AccountToken != account.AccountToken)
                return;
            session.ThrowIfCancellationRequested();
            var token = session.AutomaticToken;
            var record = historyRecordDto.ToHistoryRecord();
            record = await historyManager.PersistServerSyncedAsync(record, token);

            if (record.IsLocalFileReady)
            {
                return;
            }
            var profile = record.ToProfile();
            await historyTransferQueue.EnqueueDownload(profile, session, forceResume: false, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger?.Write($"[HistoryService] Failed to process remote history change: {ex.Message}");
        }
    }

    protected override async Task HandleClipboard(ClipboardMetaInfomation clipboardMetaInfomation, Profile profile, CancellationToken token)
    {
        var filterConfig = configManager.GetConfig<ClipboardOwnerFilterConfig>();
        if (ClipboardOwnerFilterHelper.ShouldFilter(filterConfig, clipboardMetaInfomation.Owner))
        {
            return;
        }

        if (profile.Type == ProfileType.Unknown || profile is GroupProfile { ContainsRootDirectory: true })
        {
            return;
        }

        if (profile is TextProfile textProfile && string.IsNullOrEmpty(textProfile.DisplayText))
        {
            return;
        }

        if (clipboardMetaInfomation.Effects.HasValue &&
            clipboardMetaInfomation.Effects.Value.HasFlag(DragDropEffects.Move))
        {
            return;
        }

        if (!configManager.GetConfig<SyncConfig>().IgnoreExcludeForSyncSuggestion)
        {
            if (clipboardMetaInfomation.ExcludeForSync ?? false)
            {
                return;
            }

            if (clipboardMetaInfomation.ExcludeForHistory ?? false)
            {
                return;
            }
        }

        await historyManager.AddLocalProfile(profile, token: token);
    }
}
