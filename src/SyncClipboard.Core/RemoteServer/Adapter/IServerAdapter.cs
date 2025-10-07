using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.RemoteServer.Adapter;

public interface IServerAdapter
{
    Task TestConnectionAsync(CancellationToken cancellationToken = default);
    void OnConfigChanged(object config, SyncConfig syncConfig);
}

public interface IServerAdapter<T> : IServerAdapter where T : IAdapterConfig<T>
{
    void OnConfigChanged(T config, SyncConfig syncConfig);

    void IServerAdapter.OnConfigChanged(object config, SyncConfig syncConfig)
    {
        OnConfigChanged((T)config, syncConfig);
    }
}