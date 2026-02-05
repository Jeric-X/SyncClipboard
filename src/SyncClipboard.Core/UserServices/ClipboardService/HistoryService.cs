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
    private HistoryConfig _historyConfig;
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

        _historySyncServer = currentServer as IOfficialSyncServer;
        if (_historySyncServer is not null)
        {
            _historySyncServer.HistoryChanged += OnRemoteHistoryChanged;
            _currentServer.PollStatusEvent += OnPollStatusChanged;
        }

        _lastSyncTime = null;
        TriggerSyncTask();
    }

    private void SetRuntimeConfig()
    {
        var runtimeHistoryConfig = new RuntimeHistoryConfig
        {
            EnableSyncHistory = _currentServer is IOfficialSyncServer && _historyConfig.EnableSyncHistory && _historyConfig.EnableHistory,
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
            trayIcon.SetStatusString(SERVICE_NAME, "Organizing local history records...", false);
            await historySyncer.RemoveRemoteHistorys(token).ConfigureAwait(false);
            trayIcon.SetStatusString(SERVICE_NAME, "Syncing Disabled.", false);
            return;
        }

        var serverTime = await _historySyncServer.GetServerTimeAsync(token).ConfigureAwait(false);
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
        if (runTimeConfig.GetConfig<RuntimeHistoryConfig>().EnableSyncHistory is false)
        {
            return;
        }

        try
        {
            var token = CancellationToken.None;
            var record = historyRecordDto.ToHistoryRecord();
            record = await historyManager.PersistServerSyncedAsync(record, token);

            if (record.IsLocalFileReady)
            {
                return;
            }
            var profile = record.ToProfile();
            await historyTransferQueue.EnqueueDownload(profile, forceResume: false, token);
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