using System.Reflection;
using SyncClipboard.Core.Attributes;

namespace SyncClipboard.Core.RemoteServer.Adapter;

public interface IAdapterConfig
{
    string DisplayIdentify { get; }
}

public interface IAdapterConfig<T> : IAdapterConfig
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