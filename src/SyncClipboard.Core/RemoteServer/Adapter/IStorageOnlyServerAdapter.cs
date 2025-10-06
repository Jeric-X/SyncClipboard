using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.RemoteServer.Adapter;

public interface IStorageOnlyServerAdapter : IPollingServerAdapter
{
    void OnConfigChanged(object config, SyncConfig syncConfig);
}

public interface IStorageOnlyServerAdapter<T> : IStorageOnlyServerAdapter where T : IAdapterConfig<T>
{
    void OnConfigChanged(T config, SyncConfig syncConfig);

    void IStorageOnlyServerAdapter.OnConfigChanged(object config, SyncConfig syncConfig)
    {
        OnConfigChanged((T)config, syncConfig);
    }
}