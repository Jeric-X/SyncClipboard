using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.UserServices;

namespace SyncClipboard.Core.ViewModels;

public partial class SyncSettingViewModel : ObservableObject
{
    [ObservableProperty]
    private bool serverEnable;
    partial void OnServerEnableChanged(bool value) => ServerConfig = ServerConfig with { SwitchOn = value };

    [ObservableProperty]
    private ServerConfig serverConfig;
    partial void OnServerConfigChanged(ServerConfig value)
    {
        _userConfig.SetConfig(ServerService.SERVER_CONFIG_KEY, ServerConfig);
        OnPropertyChanged(nameof(ServerUserName));
        OnPropertyChanged(nameof(ServerPassword));
        OnPropertyChanged(nameof(ServerPort));
    }

    public string ServerUserName => ServerConfig.UserName;
    public string ServerPassword => ServerConfig.Password;
    public ushort ServerPort => ServerConfig.Port;

    private readonly UserConfig2 _userConfig;

    public SyncSettingViewModel(UserConfig2 userConfig)
    {
        _userConfig = userConfig;
        _userConfig.ListenConfig<ServerConfig>(ServerService.SERVER_CONFIG_KEY, LoadFromConfig);
        serverConfig = _userConfig.GetConfig<ServerConfig>(ServerService.SERVER_CONFIG_KEY) ?? new();
        serverEnable = serverConfig.SwitchOn;
    }

    private void LoadFromConfig(object? config)
    {
        var newConfig = config as ServerConfig ?? new();
        ServerEnable = newConfig.SwitchOn;
    }

    public string? SetServerConfig(string portString, string username, string password)
    {
        if (!ushort.TryParse(portString, out var port))
        {
            return "端口号的范围是1~65535";
        }
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            return "用户名或密码不能为空";
        }

        ServerConfig = ServerConfig with { Password = password, Port = port, UserName = username };

        return null;
    }
}
