using SyncClipboard.Core.Clipboard;
using System.Reflection;

namespace SyncClipboard.Test;

[AttributeUsage(AttributeTargets.Method)]
public class PlatformServiceProviderDataSource : ServiceProviderDataSourceBase
{
    public override IEnumerable<object[]> GetData(MethodInfo methodInfo)
    {
        return new[]
        {
            new object[] { typeof(IClipboardSetter<TextProfile>) }
        };
    }
}
