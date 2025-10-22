namespace SyncClipboard.Core.RemoteServer.Adapter;

public interface IEventServerAdapter : IStorageBasedServerAdapter
{
    public void StartListening();
    public void StopListening();
    event Action<ClipboardProfileDTO>? ProfileDtoChanged;
    event Action<Exception?> ServerDisconnected;
    event Action ServerConnected;
}
