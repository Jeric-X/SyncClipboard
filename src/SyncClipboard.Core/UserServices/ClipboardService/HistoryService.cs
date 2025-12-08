using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Server.Core.Models;

namespace SyncClipboard.Core.UserServices.ClipboardService;

public class HistoryService(
    HistoryManager historyManager,
    ConfigManager configManager,
    RemoteClipboardServerFactory remoteServerFactory) : ClipboardHander
{
    private IHistorySyncServer? _historySyncServer;

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
        base.StopSerivce();
        remoteServerFactory.CurrentServerChanged -= OnServerChanged;
        if (_historySyncServer != null)
        {
            _historySyncServer.HistoryChanged -= OnRemoteHistoryChanged;
            _historySyncServer = null;
        }
    }

    private void OnServerChanged(object? sender, EventArgs e)
    {
        if (_historySyncServer != null)
        {
            _historySyncServer.HistoryChanged -= OnRemoteHistoryChanged;
            _historySyncServer = null;
        }

        var currentServer = remoteServerFactory.Current;
        if (currentServer is IHistorySyncServer historySyncServer)
        {
            _historySyncServer = historySyncServer;
            _historySyncServer.HistoryChanged += OnRemoteHistoryChanged;
        }
    }

    private async void OnRemoteHistoryChanged(HistoryRecordDto historyRecordDto)
    {
        try
        {
            var record = historyRecordDto.ToHistoryRecord();
            await historyManager.PersistServerSyncedAsync(record, CancellationToken.None);
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

        await historyManager.AddLocalProfile(profile, token);
    }
}