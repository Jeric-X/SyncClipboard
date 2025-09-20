using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.UserServices.RemoteClipboardServer;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Commons;

namespace SyncClipboard.Core.Factories;

public class RemoteClipboardServerFactory
{
    private readonly IServiceProvider _serviceProvider;
    private IRemoteClipboardServer? _current;

    public RemoteClipboardServerFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 获取当前的远程剪贴板服务器实例
    /// </summary>
    public IRemoteClipboardServer? Current
    {
        get
        {
            if (_current == null)
            {
                _current = CreateDefaultServer();
            }
            return _current;
        }
    }

    /// <summary>
    /// 创建WebDav服务器实例
    /// </summary>
    public IRemoteClipboardServer CreateWebDavServer()
    {
        var logger = _serviceProvider.GetRequiredService<ILogger>();
        var cacheManager = _serviceProvider.GetRequiredService<ILocalFileCacheManager>();
        var configManager = _serviceProvider.GetRequiredService<ConfigManager>();
        var appConfig = _serviceProvider.GetRequiredService<IAppConfig>();
        
        return new WebDavRemoteClipboardServer(logger, cacheManager, configManager, appConfig);
    }

    /// <summary>
    /// 创建默认服务器实例（当前为WebDav）
    /// </summary>
    public IRemoteClipboardServer CreateDefaultServer()
    {
        return CreateWebDavServer();
    }

    /// <summary>
    /// 重新创建服务器实例
    /// </summary>
    public void RecreateServer()
    {
        var oldServer = _current;
        
        // 如果有旧服务器，清理资源
        if (oldServer != null)
        {
            oldServer.Dispose();
        }
        
        _current = null; // 强制重新创建
    }
}