using SyncClipboard.Core.Clipboard;
using System.Reflection;

namespace SyncClipboard.Test;

[AttributeUsage(AttributeTargets.Method)]
public class PlatformServiceProviderDataSource : ServiceProviderDataSourceBase
{
    public override IEnumerable<object[]> GetData(MethodInfo methodInfo)
    {
        Type[] requiredService = {
            typeof(IClipboardFactory),
            typeof(IClipboardSetter<TextProfile>),
            typeof(IClipboardSetter<FileProfile>),
            typeof(IClipboardSetter<ImageProfile>)
        };

        List<object[]> res = new();

        foreach (Type serviceType in requiredService)
        {
            res.Add(new object[] { serviceType });
        }

        return res;
    }
}
