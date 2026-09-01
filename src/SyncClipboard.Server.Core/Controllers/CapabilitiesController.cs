using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncClipboard.Server.Core.Models;

namespace SyncClipboard.Server.Core.Controllers;

[ApiController]
[Route("api/capabilities")]
[Authorize]
[Tags("SyncClipboard")]
public class CapabilitiesController : ControllerBase
{
    private static readonly RealtimeCapabilitiesDto CurrentCapabilities = new()
    {
        SignalR = true,
        Push = new PushCapabilitiesDto { Fcm = false }
    };

    [HttpGet]
    public ActionResult<RealtimeCapabilitiesDto> Get()
    {
        return Ok(CurrentCapabilities);
    }
}
