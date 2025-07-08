using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using System.Net;

namespace SyncClipboard.Core.Commons;

public static class ProxyManager
{
    private static IWebProxy? systemProxy;
    public static event Action? GlobalProxyChanged;

    public static void SetProxy(ProxyConfig proxyConfig)
    {
        systemProxy ??= HttpClient.DefaultProxy;
        try
        {
            HttpClient.DefaultProxy = proxyConfig.Type switch
            {
                ProxyType.System => systemProxy,
                ProxyType.Custom => new WebProxy(proxyConfig.Address),
                _ => new WebProxy()
            };
        }
        catch (Exception ex)
        {
            AppCore.Current.Logger.Write("Proxy", ex.Message);
            AppCore.Current.Notification.SendText(I18n.Strings.FailedToSetProxy, ex.Message);
            HttpClient.DefaultProxy = new WebProxy(); // Fallback to no proxy
        }
        GlobalProxyChanged?.Invoke();
    }

    public static void Init(ConfigManager configManager)
    {
        systemProxy ??= HttpClient.DefaultProxy;
        configManager.GetAndListenConfig<ProxyConfig>(SetProxy);
    }
}
