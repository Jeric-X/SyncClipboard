using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.UserServices;
using System.ComponentModel;

namespace SyncClipboard.Core.ViewModels;

public partial class SyncSettingViewModel : ObservableObject
{
    [ObservableProperty]
    bool serverEnable;

    private readonly UserConfig2 _userConfig;

    public SyncSettingViewModel(UserConfig2 userConfig)
    {
        _userConfig = userConfig;
        _userConfig.ListenConfig<ServerConfig>(ServerService.SERVER_CONFIG_KEY, LoadFromConfig);
        serverEnable = _userConfig.GetConfig<ServerConfig>(ServerService.SERVER_CONFIG_KEY)?.SwitchOn ?? false;
    }

    private void LoadFromConfig(object? config)
    {
        var newConfig = config as ServerConfig ?? new();
        ServerEnable = newConfig.SwitchOn;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        _userConfig.SetConfig(ServerService.SERVER_CONFIG_KEY, new ServerConfig() { SwitchOn = ServerEnable });
        base.OnPropertyChanged(e);
    }
}
