using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Services.Notifications.Fcm;

namespace SyncClipboard.Server.Core.Controllers;

[ApiController]
[Route("api/capabilities")]
[Authorize]
[Tags("SyncClipboard")]
public class CapabilitiesController(IFcmPushClient fcmClient) : ControllerBase
{
    [HttpGet]
    public ActionResult<RealtimeCapabilitiesDto> Get()
    {
        return Ok(new RealtimeCapabilitiesDto
        {
            SignalR = true,
            Push = new PushCapabilitiesDto { Fcm = fcmClient.IsAvailable }
        });
    }
}
