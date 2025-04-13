using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract;
using SyncClipboard.Abstract.Notification;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Server.Core;

namespace SyncClipboard.Core.UserServices.ServerService;

public class ServerService : Service
{
    Microsoft.AspNetCore.Builder.WebApplication? app;
    public readonly static string SERVICE_NAME = I18n.Strings.Server;
    public const string LOG_TAG = "INNERSERVER";

    private readonly ConfigManager _configManager;
    private ServerConfig _serverConfig = new();
    private ProgramConfig _programConfig = new();
    private readonly ToggleMenuItem _toggleMenuItem;

    private readonly IServiceProvider _serviceProvider;
    private readonly IContextMenu _contextMenu;
    private readonly ILogger _logger;
    private readonly ITrayIcon _trayIcon;

    private INotification NotificationManager => _serviceProvider.GetRequiredService<INotification>();

    public ServerService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _trayIcon = serviceProvider.GetRequiredService<ITrayIcon>();
        _logger = serviceProvider.GetRequiredService<ILogger>();
        _configManager = serviceProvider.GetRequiredService<ConfigManager>();
        _contextMenu = serviceProvider.GetRequiredService<IContextMenu>();
        _toggleMenuItem = new ToggleMenuItem(
            SERVICE_NAME,
            _serverConfig.SwitchOn,
            (status) =>
            {
                _configManager.SetConfig(_serverConfig with { SwitchOn = status });
            }
        );
    }

    protected override void StartService()
    {
        _configManager.ListenConfig<ServerConfig>(ConfigChanged);
        _configManager.ListenConfig<ProgramConfig>(DiagnoseModeChanged);
        _serverConfig = _configManager.GetConfig<ServerConfig>();
        _programConfig = _configManager.GetConfig<ProgramConfig>();
        _contextMenu.AddMenuItem(_toggleMenuItem, SyncService.ContextMenuGroupName);
        RestartServer();
    }

    private void ConfigChanged(ServerConfig config)
    {
        if (config != _serverConfig)
        {
            _serverConfig = config;
            RestartServer();
        }
    }

    private void DiagnoseModeChanged(ProgramConfig config)
    {
        if (config.DiagnoseMode != _programConfig.DiagnoseMode)
        {
            _programConfig = config;
            RestartServer();
        }
    }

    public async void RestartServer()
    {
        _toggleMenuItem.Checked = _serverConfig.SwitchOn;
        StopSerivce();
        if (_serverConfig.SwitchOn)
        {
            try
            {
                using var _ = app = await Web.StartAsync(
                    new ServerPara(
                        _serverConfig.Port,
                        Env.AppDataDirectory,
                        _serverConfig.UserName,
                        _serverConfig.Password,
                        _serverConfig.ClientMixedMode,
                        _programConfig.DiagnoseMode,
                        _serviceProvider
                    )
                );
                _trayIcon.SetStatusString(SERVICE_NAME, "Running.", false);
            }
            catch (Exception ex)
            {
                _logger.Write(LOG_TAG, ex.ToString());
                _trayIcon.SetStatusString(SERVICE_NAME, ex.Message, true);
                NotificationManager.SendText(I18n.Strings.FailedToStartServer, ex.Message);
            }
        }
    }

    protected override void StopSerivce()
    {
        _trayIcon.SetStatusString(SERVICE_NAME, "Stopped.");
        app?.StopAsync();
    }
}
