using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Core.Utilities.Runner;
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
    private readonly ITrayIcon trayIcon;
    private readonly HistoryTransferQueue historyTransferQueue;
    private bool _enableSyncHistory;
    protected override bool EnableToggleMenuItem => false;

    public HistoryService(
        [FromKeyedServices(Env.RuntimeConfigName)] ConfigBase runtimeConfig,
        HistoryManager historyManager,
        ConfigManager configManager,
        RemoteClipboardServerFactory remoteServerFactory,
        HistorySyncer historySyncer,
        ITrayIcon trayIcon,
        HistoryTransferQueue historyTransferQueue)
    {
        this.runTimeConfig = runtimeConfig;
        this.historyManager = historyManager;
        this.configManager = configManager;
        this.remoteServerFactory = remoteServerFactory;
        this.historySyncer = historySyncer;
        this.trayIcon = trayIcon;
        this.historyTransferQueue = historyTransferQueue;
        _enableSyncHistory = configManager.GetConfig<HistoryConfig>().EnableSyncHistory;
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
        base.StartService();
        remoteServerFactory.CurrentServerChanged += OnServerChanged;
        OnServerChanged(null, EventArgs.Empty);
    }

    protected override void StopSerivce()
    {
        _syncingTask.Cancel();
        base.StopSerivce();
        remoteServerFactory.CurrentServerChanged -= OnServerChanged;
        UnsubscribeFromServer();
    }

    private void UnsubscribeFromServer()
    {
        if (_historySyncServer != null)
        {
            _historySyncServer.HistoryChanged -= OnRemoteHistoryChanged;
            _historySyncServer = null;
        }
        if (_currentServer != null)
        {
            _currentServer.PollStatusEvent -= OnPollStatusChanged;
            _currentServer = null;
        }
    }

    private void OnServerChanged(object? sender, EventArgs e)
    {
        UnsubscribeFromServer();

        var currentServer = remoteServerFactory.Current;
        _currentServer = currentServer;
        SetRuntimeConfig();

        if (currentServer is not IOfficialSyncServer historySyncServer)
        {
            trayIcon.SetStatusString(SERVICE_NAME, "Syncing Disabled.", false);
            return;
        }

        _historySyncServer = historySyncServer;
        _historySyncServer.HistoryChanged += OnRemoteHistoryChanged;

        // 订阅服务器状态变化事件，用于检测重连
        _currentServer.PollStatusEvent += OnPollStatusChanged;

        _lastSyncTime = null;
        TriggerSyncTask();
    }

    private void SetRuntimeConfig()
    {
        var runtimeHistoryConfig = new RuntimeHistoryConfig
        {
            EnableSyncHistory = _currentServer is IOfficialSyncServer && _enableSyncHistory
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
        if (e.Status == PollStatus.StoppedDueToNetworkIssues && _enableSyncHistory)
        {
            trayIcon.SetStatusString(SERVICE_NAME, $"History synchronization failed. Last sync time: {_lastSyncTime?.LocalDateTime:g}", error: true);
            _syncingTask.Cancel();
        }
    }

    private async void TriggerSyncTask()
    {
        try
        {
            await _syncingTask.Run();
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                return;
            }
            trayIcon.SetStatusString(SERVICE_NAME, $"History synchronization failed. Last sync time: {_lastSyncTime?.LocalDateTime:g}", error: true);
            Logger?.Write($"[{LOG_TAG}] 同步所有历史记录失败: {ex.Message}");
        }
    }

    private void OnHistoryConfigChanged(HistoryConfig cfg)
    {
        var newEnableSyncHistory = cfg.EnableSyncHistory;
        if (newEnableSyncHistory == _enableSyncHistory)
        {
            return;
        }

        _enableSyncHistory = newEnableSyncHistory;
        SetRuntimeConfig();
        if (!_enableSyncHistory)
        {
            trayIcon.SetStatusString(SERVICE_NAME, "Syncing Disabled.", false);
        }
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
        if (!_enableSyncHistory || _historySyncServer == null)
        {
            await historySyncer.RemoveRemoteHistorys(token).ConfigureAwait(false);
            return;
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
            await SaveServerTime(token).ConfigureAwait(false);
            trayIcon.SetStatusString(SERVICE_NAME, $"Synchronized. Last sync time {_lastSyncTime?.LocalDateTime:g}", false);
            await Task.Delay(TimeSpan.FromMinutes(1), token).ConfigureAwait(false);
        }
    }

    private async Task SaveServerTime(CancellationToken token)
    {
        if (_historySyncServer != null)
        {
            try
            {
                _lastSyncTime = await _historySyncServer.GetServerTimeAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { }
        }
    }

    private async void OnRemoteHistoryChanged(HistoryRecordDto historyRecordDto)
    {
        if (!_enableSyncHistory)
        {
            return;
        }

        try
        {
            var token = CancellationToken.None;
            var record = historyRecordDto.ToHistoryRecord();
            record = await historyManager.PersistServerSyncedAsync(record, token);

            // 如果未启用同步剪贴板或未启用拉取，使用 historyTransferQueue 下载
            var syncConfig = configManager.GetConfig<SyncConfig>();
            if (!syncConfig.SyncSwitchOn || !syncConfig.PullSwitchOn)
            {
                if (record.IsLocalFileReady)
                {
                    return;
                }
                var profile = record.ToProfile();
                await historyTransferQueue.EnqueueDownload(profile, forceResume: false, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.Write($"[HistoryService] Failed to process remote history change: {ex.Message}");
        }
    }

    protected override async Task HandleClipboard(ClipboardMetaInfomation clipboardMetaInfomation, Profile profile, CancellationToken token)
    {
        if (profile.Type == ProfileType.Unknown)
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