using System.Security.Claims;
using SyncClipboard.Server.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SyncClipboard.Server.Core.Models;

namespace SyncClipboard.Server.Core.Hubs;

public interface ISyncClipboardClient
{
    Task RemoteProfileChanged(ProfileDto profile);
    Task RemoteHistoryChanged(HistoryRecordDto historyRecordDto);
}

[Authorize]
public class SyncClipboardHub(ServerSettingsStore settings) : Hub<ISyncClipboardClient>
{
    public override Task OnConnectedAsync()
    {
        var revoked = settings.GetSessionRevocation(Context.User?.FindFirstValue(ClaimTypes.Sid));
        var registration = revoked.Register(Context.Abort);
        Context.ConnectionAborted.Register(registration.Dispose);
        return base.OnConnectedAsync();
    }
}
