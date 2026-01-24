using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.RemoteServer.Adapter;

public interface IServerAdapter
{
    Task TestConnectionAsync(CancellationToken cancellationToken = default);
    void SetConfig(object config, SyncConfig syncConfig);
    void ApplyConfig();
}

public interface IServerAdapter<T> : IServerAdapter where T : IAdapterConfig<T>
{
    void SetConfig(T config, SyncConfig syncConfig);

    void IServerAdapter.SetConfig(object config, SyncConfig syncConfig)
    {
        SetConfig((T)config, syncConfig);
    }
}