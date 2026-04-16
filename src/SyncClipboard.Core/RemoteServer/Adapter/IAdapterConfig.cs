using System.Reflection;
using SyncClipboard.Core.Attributes;

namespace SyncClipboard.Core.RemoteServer.Adapter;

public interface IAdapterConfig
{
    string NameSuggestion { get; }
    string CustomName { get; set; }
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

    static int Priority
    {
        get
        {
            var type = typeof(T);
            var accountAttribute = type.GetCustomAttribute<AccountConfigTypeAttribute>();
            if (accountAttribute != null)
            {
                return accountAttribute.Priority;
            }
            return int.MaxValue;
        }
    }
}