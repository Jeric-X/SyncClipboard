using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.RemoteServer;

namespace SyncClipboard.Core.ViewModels;

public partial class HistorySettingViewModel : ObservableObject
{
    // Loading saved values must not persist partial settings or invoke platform services.
    private readonly bool _isInitializing = true;

    private readonly ConfigManager _configManager;
    private readonly HistoryManager _historyManager;
    private readonly IMainWindowDialog _dialog;
    private readonly RemoteClipboardServerFactory _remoteServerFactory;

    public HistorySettingViewModel(ConfigManager configManager, HistoryManager historyManager, IMainWindowDialog dialog, RemoteClipboardServerFactory remoteServerFactory)
    {
        _configManager = configManager;
        _historyManager = historyManager;
        _dialog = dialog;
        _remoteServerFactory = remoteServerFactory;

        var config = configManager.GetConfig<HistoryConfig>();
        EnableHistory = config.EnableHistory;
        EnableSyncHistory = config.EnableSyncHistory;
        AutoDeleteMissingLocalFiles = config.AutoDeleteMissingLocalFiles;
        MaxItemCount = config.MaxItemCount;
        HistoryRetentionMinutes = config.HistoryRetentionMinutes;

        _isInitializing = false;
        UpdateServerSyncSupported();

        configManager.ListenConfig<HistoryConfig>(OnHistoryConfigChanged);
        _remoteServerFactory.CurrentServerChanged += OnCurrentServerChanged;
    }

    private void OnHistoryConfigChanged(HistoryConfig config)
    {
        EnableHistory = config.EnableHistory;
        EnableSyncHistory = config.EnableSyncHistory;
        AutoDeleteMissingLocalFiles = config.AutoDeleteMissingLocalFiles;
        MaxItemCount = config.MaxItemCount;
        HistoryRetentionMinutes = config.HistoryRetentionMinutes;
    }

    private void OnCurrentServerChanged(object? sender, EventArgs e)
    {
        UpdateServerSyncSupported();
    }

    private void UpdateServerSyncSupported()
    {
        ServerSyncSupported = _remoteServerFactory.Current is IOfficialSyncServer;
    }

    private HistoryConfig GetCurrentRecord()
    {
        return new HistoryConfig
        {
            EnableHistory = EnableHistory,
            EnableSyncHistory = EnableSyncHistory,
            AutoDeleteMissingLocalFiles = AutoDeleteMissingLocalFiles,
            MaxItemCount = MaxItemCount,
            HistoryRetentionMinutes = HistoryRetentionMinutes
        };
    }

    [ObservableProperty]
    public partial bool EnableHistory { get; set; }

    partial void OnEnableHistoryChanged(bool value)
    {
        if (!_isInitializing) _configManager.SetConfig(GetCurrentRecord() with { EnableHistory = value });
    }

    [ObservableProperty]
    public partial bool EnableSyncHistory { get; set; }

    partial void OnEnableSyncHistoryChanged(bool value)
    {
        if (!_isInitializing) _configManager.SetConfig(GetCurrentRecord() with { EnableSyncHistory = value });
    }

    [ObservableProperty]
    public partial bool AutoDeleteMissingLocalFiles { get; set; }

    partial void OnAutoDeleteMissingLocalFilesChanged(bool value)
    {
        if (!_isInitializing) _configManager.SetConfig(GetCurrentRecord() with { AutoDeleteMissingLocalFiles = value });
    }

    [ObservableProperty]
    public partial uint MaxItemCount { get; set; }

    partial void OnMaxItemCountChanged(uint value)
    {
        if (!_isInitializing) _configManager.SetConfig(GetCurrentRecord() with { MaxItemCount = value });
    }

    [ObservableProperty]
    public partial uint HistoryRetentionMinutes { get; set; }

    partial void OnHistoryRetentionMinutesChanged(uint value)
    {
        if (!_isInitializing) _configManager.SetConfig(GetCurrentRecord() with { HistoryRetentionMinutes = value });
    }

    [ObservableProperty]
    public partial bool ServerSyncSupported { get; set; }

    [RelayCommand]
    private async Task ClearLocalHistoryAsync()
    {
        var confirmed = await _dialog.ShowConfirmationAsync(I18n.Strings.ClearLocalHistory, I18n.Strings.ClearLocalHistoryConfirmMessage).ConfigureAwait(false);
        if (!confirmed) return;
        await _historyManager.ClearAllLocalAsync();
    }
}
