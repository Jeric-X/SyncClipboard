using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.ViewModels;

public partial class HistorySettingViewModel : ObservableObject
{
    private readonly ConfigManager _configManager;

    public HistorySettingViewModel(ConfigManager configManager)
    {
        _configManager = configManager;
        var config = configManager.GetConfig<HistoryConfig>();
        enableHistory = config.EnableHistory;
        maxItemCount = config.MaxItemCount;
        closeWhenLostFocus = config.CloseWhenLostFocus;
        configManager.ListenConfig<HistoryConfig>(OnHistoryConfigChanged);
    }

    private void OnHistoryConfigChanged(HistoryConfig config)
    {
        EnableHistory = config.EnableHistory;
        MaxItemCount = config.MaxItemCount;
        CloseWhenLostFocus = config.CloseWhenLostFocus;
    }

    private HistoryConfig GetCurrentRecord()
    {
        return new HistoryConfig
        {
            EnableHistory = EnableHistory,
            MaxItemCount = MaxItemCount,
            CloseWhenLostFocus = CloseWhenLostFocus
        };
    }

    [ObservableProperty]
    private bool enableHistory;
    partial void OnEnableHistoryChanged(bool value) => _configManager.SetConfig(GetCurrentRecord() with { EnableHistory = value });

    [ObservableProperty]
    private uint maxItemCount;
    partial void OnMaxItemCountChanged(uint value) => _configManager.SetConfig(GetCurrentRecord() with { MaxItemCount = value });

    [ObservableProperty]
    private bool closeWhenLostFocus;
    partial void OnCloseWhenLostFocusChanged(bool value) => _configManager.SetConfig(GetCurrentRecord() with { CloseWhenLostFocus = value });
}
