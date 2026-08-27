using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.ViewModels;

public partial class CliboardAssistantViewModel : ObservableObject
{
    [ObservableProperty]
    private bool easyCopyImageSwitchOn;
    partial void OnEasyCopyImageSwitchOnChanged(bool value) => ClipboardAssistConfig = ClipboardAssistConfig with { EasyCopyImageSwitchOn = value };

    [ObservableProperty]
    private bool downloadWebImage;
    partial void OnDownloadWebImageChanged(bool value) => ClipboardAssistConfig = ClipboardAssistConfig with { DownloadWebImage = value };

    [ObservableProperty]
    private bool convertSwitchOn;
    partial void OnConvertSwitchOnChanged(bool value) => ClipboardAssistConfig = ClipboardAssistConfig with { ConvertSwitchOn = value };

    [ObservableProperty]
    private ClipboardAssistConfig clipboardAssistConfig;
    partial void OnClipboardAssistConfigChanged(ClipboardAssistConfig value)
    {
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
        clipboardAssistConfig = _configManager.GetConfig<ClipboardAssistConfig>();
        easyCopyImageSwitchOn = clipboardAssistConfig.EasyCopyImageSwitchOn;
        downloadWebImage = clipboardAssistConfig.DownloadWebImage;
        convertSwitchOn = clipboardAssistConfig.ConvertSwitchOn;
    }
}
