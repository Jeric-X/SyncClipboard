using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.UserServices;

public class ServerService : Service
{
    Microsoft.AspNetCore.Builder.WebApplication? app;
    public const string SERVICE_NAME = "内置服务器";
    public const string LOG_TAG = "INNERSERVER";

    private readonly UserConfig2 _userConfig;
    private ServerConfig _serverConfig = new();
    private readonly ToggleMenuItem _toggleMenuItem;

    private readonly IContextMenu _contextMenu;

    public ServerService(UserConfig2 userConfig, IContextMenu contextMenu)
    {
        _userConfig = userConfig;
        _contextMenu = contextMenu;
        _toggleMenuItem = new ToggleMenuItem(
            SERVICE_NAME,
            _serverConfig.SwitchOn,
            (status) =>
            {
                _userConfig.SetConfig(ConfigKey.Server, _serverConfig with { SwitchOn = status });
            }
        );
    }

    protected override void StartService()
    {
        _userConfig.ListenConfig<ServerConfig>(ConfigKey.Server, ConfigChanged);
        _serverConfig = _userConfig.GetConfig<ServerConfig>(ConfigKey.Server) ?? new();
        _contextMenu.AddMenuItem(_toggleMenuItem, SyncService.ContextMenuGroupName);
        RestartServer();
    }

    private void ConfigChanged(object? config)
    {
        var newConfig = config as ServerConfig;
        if (newConfig != _serverConfig)
        {
            _serverConfig = newConfig ?? new();
            RestartServer();
        }
    }

    public void RestartServer()
    {
        _toggleMenuItem.Checked = _serverConfig.SwitchOn;
        app?.StopAsync();
        if (_serverConfig.SwitchOn)
        {
            app = Server.Program.Start(
                _serverConfig.Port,
                Env.Directory,
                _serverConfig.UserName,
                _serverConfig.Password
            );
        }
    }

    protected override void StopSerivce()
    {
        app?.StopAsync();
    }
}
