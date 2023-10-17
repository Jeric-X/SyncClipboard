using SyncClipboard.Abstract;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using System.Reflection;

namespace SyncClipboard.Test;

[AttributeUsage(AttributeTargets.Method)]
public class SystemServiceProviderDataSource : ServiceProviderDataSourceBase
{
    public override IEnumerable<object[]> GetData(MethodInfo methodInfo)
    {
        Type[] requiredService = {
            typeof(IServiceProvider),
            typeof(ConfigManager),
            typeof(IAppConfig),
            typeof(ILogger),
            typeof(IWebDav),
            typeof(IHttp),
            typeof(INotification)
        };

        List<object[]> res = new();

        foreach (Type serviceType in requiredService)
        {
            res.Add(new object[] { serviceType });
        }

        return res;
    }
}