using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Services;

namespace SyncClipboard.Server.Core.Controllers;

[ApiController]
[Authorize(Policy = "ServerAdministrator")]
[Route("api/admin")]
public sealed class AdminController(ServerSettingsStore settings) : ControllerBase
{
    [HttpGet("settings")]
    public AppSettings GetSettings() => settings.Value;

    [HttpPut("settings")]
    public IActionResult SetSettings(AppSettings history)
    {
        settings.SetHistory(history);
        return NoContent();
    }

    [HttpGet("users")]
    public IActionResult ListUsers() => Ok(settings.ListAccounts());

    [HttpPost("users")]
    public IActionResult CreateUser(AccountUpdate update) => SaveUser(null, update);

    [HttpPut("users/{id}")]
    public IActionResult UpdateUser(string id, AccountUpdate update) => SaveUser(id, update);

    private IActionResult SaveUser(string? id, AccountUpdate update)
    {
        try
        {
            return Ok(new { Id = settings.SetAccount(id, update) });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("users/{id}")]
    public IActionResult DeleteUser(string id)
    {
        if (id == ServerSettingsStore.AdministratorId)
            return BadRequest("The administrator account cannot be removed.");
        return settings.RemoveAccount(id) ? NoContent() : NotFound();
    }
}
