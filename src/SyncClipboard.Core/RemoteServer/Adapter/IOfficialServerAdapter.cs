namespace SyncClipboard.Core.RemoteServer.Adapter;

public interface IOfficialServerAdapter : IStorageBasedServerAdapter
{
    public void StartListening();
    public void StopListening();
    event Action<ProfileDto>? ProfileDtoChanged;
    event Action<Exception?> ServerDisconnected;
    event Action ServerConnected;

    Task SetCurrentProfile(ProfileDto dto, CancellationToken cancellationToken = default);
    Task<ProfileDto?> GetCurrentProfileAsync(CancellationToken cancellationToken = default);
}
