namespace SyncClipboard.Core.RemoteServer.Adapter;

public interface IAdapterConfig
{
    string NameSuggestion { get; }
    string CustomName { get; set; }
}

public interface IAdapterConfig<T> : IAdapterConfig
{
    static string TypeName => AccountConfigRegistry.GetRegistration(typeof(T)).TypeName;

    static int Priority => AccountConfigRegistry.GetRegistration(typeof(T)).Priority;
}
