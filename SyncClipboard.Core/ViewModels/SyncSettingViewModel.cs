using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.UserServices;
using System.ComponentModel;

namespace SyncClipboard.Core.ViewModels;

public partial class SyncSettingViewModel : ObservableObject
{
    private bool _readyForSaveServer = true;

    [ObservableProperty]
    private bool serverEnable;

    [ObservableProperty]
    private ushort serverPort;

    [ObservableProperty]
    private string serverAuthUserName;

    [ObservableProperty]
    private string serverAuthPassword;

    [ObservableProperty]
    private ServerConfig serverConfig;

    private readonly UserConfig2 _userConfig;

    public SyncSettingViewModel(UserConfig2 userConfig)
    {
        _userConfig = userConfig;
        _userConfig.ListenConfig<ServerConfig>(ServerService.SERVER_CONFIG_KEY, LoadFromConfig);
        serverConfig = _userConfig.GetConfig<ServerConfig>(ServerService.SERVER_CONFIG_KEY) ?? new();
        serverEnable = serverConfig.SwitchOn;
        serverPort = serverConfig.Port;
        serverAuthUserName = serverConfig.UserName;
        serverAuthPassword = serverConfig.Password;
    }

    private void LoadFromConfig(object? config)
    {
        var newConfig = config as ServerConfig ?? new();
        ServerEnable = newConfig.SwitchOn;
        ServerPort = newConfig.Port;
        ServerAuthUserName = newConfig.UserName;
        ServerAuthPassword = newConfig.Password;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        _userConfig.SetConfig(ServerService.SERVER_CONFIG_KEY, new ServerConfig()
        {
            SwitchOn = ServerEnable,
            Port = ServerPort,
            UserName = ServerAuthUserName,
            Password = ServerAuthPassword
        });
        base.OnPropertyChanged(e);
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

        _readyForSaveServer = false;
        ServerPort = port;
        ServerAuthUserName = username;
        _readyForSaveServer = true;
        ServerAuthPassword = password;
        return null;
    }
}
