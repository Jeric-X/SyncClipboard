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
public class SyncClipboardHub : Hub<ISyncClipboardClient>
{
}
