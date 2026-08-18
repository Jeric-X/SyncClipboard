using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using System.Net;

namespace SyncClipboard.Core.Commons;

public static class ProxyManager
{
    private static IWebProxy? systemProxy;
    public static event Action? GlobalProxyChanged;

    public static IWebProxy CurrentProxy { get; private set; } = new WebProxy();

    public static void SetProxy(ProxyConfig proxyConfig)
    {
        systemProxy ??= HttpClient.DefaultProxy;
        try
        {
            CurrentProxy = proxyConfig.Type switch
            {
                ProxyType.System => systemProxy,
                ProxyType.Custom => new WebProxy(proxyConfig.Address), // 不设 BypassProxyOnLocal，否则局域网地址会被绕过
                _ => new WebProxy()
            };
            HttpClient.DefaultProxy = CurrentProxy;
        }
        catch (Exception ex)
        {
            AppCore.Current.Logger.Write("Proxy", ex.Message);
            AppCore.Current.NotificationManager.ShowText(I18n.Strings.FailedToSetProxy, ex.Message);
            CurrentProxy = new WebProxy(); // Fallback to no proxy
            HttpClient.DefaultProxy = CurrentProxy;
        }
        GlobalProxyChanged?.Invoke();
    }

    public static void Init(ConfigManager configManager)
    {
        systemProxy ??= HttpClient.DefaultProxy;
        configManager.GetAndListenConfig<ProxyConfig>(SetProxy);
    }
}
