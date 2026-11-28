using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SyncClipboard.Server.Core.Hubs;
using SyncClipboard.Server.Core.Models;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Authorization;
using SyncClipboard.Server.Core.Services.History;
using SyncClipboard.Server.Core.Services;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Server.Core.Controllers;

[ApiController]
[Authorize]
[Tags("SyncClipboard")]
public class SyncClipboardController(
    IHubContext<SyncClipboardHub, ISyncClipboardClient> _hubContext,
    ServerEnvProvider _serverEnv,
    HistoryService _historyService) : ControllerBase
{
    private string UserId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value;

    private string ClipboardDir => _serverEnv.GetClipboardDir(UserId);

    private static void EnsureFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    private static void SafeDeleteFolder(string path)
    {
        if (Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, true);
            }
            catch { }
        }
    }

    [HttpGet("api/time")]
    public DateTimeOffset GetServerTime()
    {
        return DateTimeOffset.Now;
    }

    [HttpGet("api/version")]
    public IActionResult GetVersion()
    {
        return Ok(SyncClipboardProperty.AppVersion);
    }

    [HttpGet("api/capabilities")]
    public ActionResult<ServerCapabilitiesDto> GetCapabilities()
    {
        return Ok(new ServerCapabilitiesDto { HistorySync = true, HistoryTags = true, HistoryTitle = true });
    }

    [AcceptVerbs("PROPFIND")]
    [Route("")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult PropfindRoot()
    {
        return Ok();
    }

    [AcceptVerbs("PROPFIND", "MKCOL")]
    [Route("file")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult FileFolderEnsure()
    {
        var path = Path.Combine(ClipboardDir, "file");
        EnsureFolder(path);
        return Ok();
    }

    [HttpDelete("file")]
    public async Task<IActionResult> DeleteFileFolder(CancellationToken token)
    {
        using var guard = await _serverEnv.LockClipboard(UserId, token);
        var path = Path.Combine(ClipboardDir, "file");
        SafeDeleteFolder(path);
        return Ok();
    }

    [HttpHead("file/{fileName}")]
    [HttpGet("file/{fileName}")]
    public async Task<IActionResult> GetFileFromFolder(string fileName, CancellationToken token)
    {
        if (ServerEnvProvider.InvalidFileName(fileName))
        {
            return BadRequest();
        }

        try
        {
            var current = _serverEnv.ReadUsableClipboard(UserId, out var uploadedAs);
            if (current is { HasData: true, DataName: not null } && !ServerEnvProvider.InvalidFileName(current.DataName)
                && (fileName == uploadedAs || ServerEnvProvider.PublicClipboard(UserId, current).DataName == fileName))
                return await GetFileInternal(Profile.GetFullPath(_serverEnv.GetPersistentDir(), current.Type, current.Hash, current.DataName));

            var path = await _historyService.GetRecentTransferFile(UserId, fileName, token);
            return await GetFileInternal(path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("file/{fileName}")]
    public async Task<IActionResult> PutFileToFolder(string fileName)
    {
        if (ServerEnvProvider.InvalidFileName(fileName))
        {
            return BadRequest();
        }
        Directory.CreateDirectory(ClipboardDir);
        var folder = Path.Combine(ClipboardDir, "file");
        var path = Path.Combine(folder, fileName);
        using var staging = new UploadDirectory(_serverEnv.GetPersistentDir());
        var temp = Path.Combine(staging.DirectoryPath, "upload");
        await using (var fs = System.IO.File.Create(temp))
            await Request.Body.CopyToAsync(fs, HttpContext.RequestAborted);
        using var guard = await _serverEnv.LockClipboard(UserId, HttpContext.RequestAborted);
        EnsureFolder(folder);
        System.IO.File.Move(temp, path, true);
        return Ok();
    }

    [HttpGet("SyncClipboard.json")]
    public async Task<ActionResult<ProfileDto>> GetSyncProfile(CancellationToken token)
    {
        if (_serverEnv.ReadUsableClipboard(UserId) is { } profile)
            return Ok(ServerEnvProvider.PublicClipboard(UserId, profile));
        return Ok(await new TextProfile(string.Empty).ToProfileDto(token));
    }

    [HttpPut("SyncClipboard.json")]
    public async Task<IActionResult> PutSyncProfile([FromBody] ProfileDto dto, CancellationToken token)
    {
        if (dto is null)
        {
            return BadRequest("dto cannot be null");
        }

        try
        {
            dto = dto with
            {
                TransferDataHash = Utility.NormalizeSHA256(dto.TransferDataHash)
            };
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }

        if (!dto.HasData && dto.TransferDataHash is not null)
        {
            return BadRequest("TransferDataHash cannot be set when HasData is false");
        }

        try
        {
            using var guard = await _serverEnv.LockClipboard(UserId, token);
            var saved = await _historyService.SaveCurrentClipboard(UserId, dto, ClipboardDir, token);
            await _hubContext.Clients.User(UserId).RemoteProfileChanged(saved);
            return Ok();
        }
        catch (FileNotFoundException)
        {
            return NotFound("Transfer data file not found");
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or InvalidOperationException)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult GetRoot()
    {
        return Ok("Server is running.");
    }

    private async Task<IActionResult> GetFileInternal(string? path)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            return NotFound();
        }
        new FileExtensionContentTypeProvider().TryGetContentType(path, out string? contentType);
        var bytes = await System.IO.File.ReadAllBytesAsync(path);
        return File(bytes, contentType ?? "application/octet-stream");
    }
}
