using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SyncClipboard.Server.Core.Hubs;

[Authorize]
public class SyncClipboardHub : Hub
{
}
