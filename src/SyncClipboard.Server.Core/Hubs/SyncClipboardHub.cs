using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SyncClipboard.Server.Core.Hubs;

public interface ISyncClipboardClient
{
    Task RemoteProfileChanged(ClipboardProfileDTO profile);
}

[Authorize]
public class SyncClipboardHub : Hub<ISyncClipboardClient>
{
}
