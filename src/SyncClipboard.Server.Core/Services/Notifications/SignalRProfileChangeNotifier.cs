using Microsoft.AspNetCore.SignalR;
using SyncClipboard.Server.Core.Hubs;

namespace SyncClipboard.Server.Core.Services.Notifications;

public class SignalRProfileChangeNotifier(
    IHubContext<SyncClipboardHub, ISyncClipboardClient> hubContext) : IProfileChangeNotifier
{
    public Task NotifyProfileChanged(
        ProfileDto profile,
        CancellationToken cancellationToken = default)
    {
        return hubContext.Clients.All.RemoteProfileChanged(profile);
    }
}
