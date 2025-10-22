using SyncClipboard.Abstract.Profiles;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using System.Reflection;

namespace SyncClipboard.Test;

[AttributeUsage(AttributeTargets.Method)]
public class PlatformServiceProviderDataSource : ServiceProviderDataSourceBase
{
    public override IEnumerable<object[]> GetData(MethodInfo methodInfo)
    {
        Type[] requiredService = [
            typeof(IClipboardFactory),
            typeof(IClipboardChangingListener),
            typeof(IClipboardSetter<TextProfile>),
            typeof(IClipboardSetter<FileProfile>),
            typeof(IClipboardSetter<ImageProfile>)
        ];

        List<object[]> res = [];

        foreach (Type serviceType in requiredService)
        {
            res.Add([serviceType]);
        }

        return res;
    }
}
