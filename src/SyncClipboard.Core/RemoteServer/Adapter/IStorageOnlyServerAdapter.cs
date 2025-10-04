namespace SyncClipboard.Core.RemoteServer.Adapter;

public interface IStorageOnlyServerAdapter : IPollingServerAdapter
{
    void OnConfigChanged(object config);
}

public interface IStorageOnlyServerAdapter<T> : IStorageOnlyServerAdapter where T : IAdapterConfig<T>
{
    void OnConfigChanged(T config);

    void IStorageOnlyServerAdapter.OnConfigChanged(object config)
    {
        OnConfigChanged((T)config);
    }
}