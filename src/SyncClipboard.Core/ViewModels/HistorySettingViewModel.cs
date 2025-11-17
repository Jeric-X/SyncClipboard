using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.History;

namespace SyncClipboard.Core.ViewModels;

public partial class HistorySettingViewModel : ObservableObject
{
    private readonly ConfigManager _configManager;
    private readonly HistoryManager _historyManager;
    private readonly IMainWindowDialog _dialog;

    public HistorySettingViewModel(ConfigManager configManager, HistoryManager historyManager, IMainWindowDialog dialog)
    {
        _configManager = configManager;
        _historyManager = historyManager;
        _dialog = dialog;
        var config = configManager.GetConfig<HistoryConfig>();
        enableHistory = config.EnableHistory;
        enableSyncHistory = config.EnableSyncHistory;
        maxItemCount = config.MaxItemCount;
        historyRetentionMinutes = config.HistoryRetentionMinutes;
        configManager.ListenConfig<HistoryConfig>(OnHistoryConfigChanged);
    }

    private void OnHistoryConfigChanged(HistoryConfig config)
    {
        EnableHistory = config.EnableHistory;
        EnableSyncHistory = config.EnableSyncHistory;
        MaxItemCount = config.MaxItemCount;
        HistoryRetentionMinutes = config.HistoryRetentionMinutes;
    }

    private HistoryConfig GetCurrentRecord()
    {
        return new HistoryConfig
        {
            EnableHistory = EnableHistory,
            EnableSyncHistory = EnableSyncHistory,
            MaxItemCount = MaxItemCount,
            HistoryRetentionMinutes = HistoryRetentionMinutes
        };
    }

    [ObservableProperty]
    private bool enableHistory;
    partial void OnEnableHistoryChanged(bool value) => _configManager.SetConfig(GetCurrentRecord() with { EnableHistory = value });

    [ObservableProperty]
    private bool enableSyncHistory;
    partial void OnEnableSyncHistoryChanged(bool value) => _configManager.SetConfig(GetCurrentRecord() with { EnableSyncHistory = value });

    [ObservableProperty]
    private uint maxItemCount;
    partial void OnMaxItemCountChanged(uint value) => _configManager.SetConfig(GetCurrentRecord() with { MaxItemCount = value });

    [ObservableProperty]
    private uint historyRetentionMinutes;
    partial void OnHistoryRetentionMinutesChanged(uint value) => _configManager.SetConfig(GetCurrentRecord() with { HistoryRetentionMinutes = value });

    [RelayCommand]
    private async Task ClearLocalHistoryAsync()
    {
        var confirmed = await _dialog.ShowConfirmationAsync(I18n.Strings.ClearLocalHistory, I18n.Strings.ClearLocalHistoryConfirmMessage);
        if (!confirmed) return;
        await _historyManager.ClearAllLocalAsync();
    }
}
