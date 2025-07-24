using NativeNotification.Interface;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using System.Reflection;

namespace SyncClipboard.Test;

[AttributeUsage(AttributeTargets.Method)]
public class SystemServiceProviderDataSource : ServiceProviderDataSourceBase
{
    public override IEnumerable<object[]> GetData(MethodInfo methodInfo)
    {
        Type[] requiredService = [
            typeof(IServiceProvider),
            typeof(ConfigManager),
            typeof(IAppConfig),
            typeof(ILogger),
            typeof(IWebDav),
            typeof(IHttp),
            typeof(INotificationManager)
        ];

        List<object[]> res = [];

        foreach (Type serviceType in requiredService)
        {
            res.Add([serviceType]);
        }

        return res;
    }
}