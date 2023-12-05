using System.Reflection;

namespace SyncClipboard.Test
{
    public abstract class ServiceProviderDataSourceBase : Attribute, ITestDataSource
    {
        public abstract IEnumerable<object[]> GetData(MethodInfo methodInfo);

        public string? GetDisplayName(MethodInfo methodInfo, object?[]? data)
        {
            if (data != null)
            {
                var obj = data[0] as Type;
                return obj?.Name;
            }

            return null;
        }
    }
}
