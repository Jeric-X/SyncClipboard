using SyncClipboard.Core.Clipboard;
using System.Reflection;

namespace SyncClipboard.Test;

[AttributeUsage(AttributeTargets.Method)]
public class SystemServiceProviderDataSource : ServiceProviderDataSourceBase
{
    public override IEnumerable<object[]> GetData(MethodInfo methodInfo)
    {
        return new[]
        {
            new object[] { typeof(IServiceProvider) }
        };
    }
}
