using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.UserServices.RemoteClipboardServer;
using SyncClipboard.Core.Commons;

namespace SyncClipboard.Core.Factories;

public class RemoteClipboardServerFactory(IServiceProvider _serviceProvider)
{
    private IRemoteClipboardServer? _current;

    public IRemoteClipboardServer Current
    {
        get
        {
            _current ??= CreateDefaultServer();
            return _current;
        }
    }

    public IRemoteClipboardServer CreateWebDavServer()
    {
        var logger = _serviceProvider.GetRequiredService<ILogger>();
        var configManager = _serviceProvider.GetRequiredService<ConfigManager>();
        var appConfig = _serviceProvider.GetRequiredService<IAppConfig>();
        var trayIcon = _serviceProvider.GetRequiredService<ITrayIcon>();

        return new WebDavRemoteClipboardServer(logger, configManager, appConfig, trayIcon);
    }

    public IRemoteClipboardServer CreateDefaultServer()
    {
        return CreateWebDavServer();
    }

    public void ResetServer()
    {
        var oldServer = _current;
        oldServer?.Dispose();
        _current = null;
    }
}