using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.ViewModels;

public partial class CliboardAssistantViewModel : ObservableObject
{
    // Loading saved values must not persist partial settings or invoke platform services.
    private readonly bool _isInitializing = true;

    [ObservableProperty]
    public partial bool EasyCopyImageSwitchOn { get; set; }

    partial void OnEasyCopyImageSwitchOnChanged(bool value)
    {
        if (_isInitializing) return;
        ClipboardAssistConfig = ClipboardAssistConfig with { EasyCopyImageSwitchOn = value };
    }

    [ObservableProperty]
    public partial bool DownloadWebImage { get; set; }

    partial void OnDownloadWebImageChanged(bool value)
    {
        if (_isInitializing) return;
        ClipboardAssistConfig = ClipboardAssistConfig with { DownloadWebImage = value };
    }

    [ObservableProperty]
    public partial bool ConvertSwitchOn { get; set; }

    partial void OnConvertSwitchOnChanged(bool value)
    {
        if (_isInitializing) return;
        ClipboardAssistConfig = ClipboardAssistConfig with { ConvertSwitchOn = value };
    }

    [ObservableProperty]
    public partial ClipboardAssistConfig ClipboardAssistConfig { get; set; }

    partial void OnClipboardAssistConfigChanged(ClipboardAssistConfig value)
    {
        if (_isInitializing) return;

        EasyCopyImageSwitchOn = value.EasyCopyImageSwitchOn;
        DownloadWebImage = value.DownloadWebImage;
        ConvertSwitchOn = value.ConvertSwitchOn;
        _configManager.SetConfig(value);
    }

    private readonly ConfigManager _configManager;
    private readonly MainViewModel _mainVM;

    [RelayCommand]
    private void SetEasyCopyImageFilter()
    {
        _mainVM.NavigateToNextLevel(PageDefinition.ClipboardOwnerFilterSetting, ClipboardOwnerFilterConfig.EasyCopyImageFilterConfigKey);
    }

    public CliboardAssistantViewModel(ConfigManager configManager, MainViewModel mainVM)
    {
        _configManager = configManager;
        _mainVM = mainVM;

        _configManager.ListenConfig<ClipboardAssistConfig>(config => ClipboardAssistConfig = config);
        ClipboardAssistConfig = _configManager.GetConfig<ClipboardAssistConfig>();
        EasyCopyImageSwitchOn = ClipboardAssistConfig.EasyCopyImageSwitchOn;
        DownloadWebImage = ClipboardAssistConfig.DownloadWebImage;
        ConvertSwitchOn = ClipboardAssistConfig.ConvertSwitchOn;
        _isInitializing = false;
    }
}
