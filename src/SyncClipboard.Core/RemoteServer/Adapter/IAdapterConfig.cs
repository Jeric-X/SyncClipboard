using System.Reflection;
using SyncClipboard.Core.Attributes;

namespace SyncClipboard.Core.RemoteServer.Adapter;

public interface IAdapterConfig<T>
{
    static string TypeName
    {
        get
        {
            var type = typeof(T);
            var accountAttribute = type.GetCustomAttribute<AccountConfigTypeAttribute>();
            if (accountAttribute != null)
            {
                return accountAttribute.GetName();
            }
            return type.Name;
        }
    }
}