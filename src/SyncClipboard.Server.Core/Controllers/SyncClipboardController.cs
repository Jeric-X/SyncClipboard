using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SyncClipboard.Server.Core.Hubs;
using Microsoft.AspNetCore.StaticFiles;
using System.Text.Json;
using SyncClipboard.Server.Core.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using SyncClipboard.Server.Core.Services.History;
using SyncClipboard.Server.Core.Services;

namespace SyncClipboard.Server.Core.Controllers;

[ApiController]
[Authorize]
public class SyncClipboardController(
    IHubContext<SyncClipboardHub> _hubContext,
    IMemoryCache _cache,
    ServerEnvProvider _serverEnv,
    HistoryService _historyService) : ControllerBase
{
    private static bool InvalidFileName(string name)
    {
        return name.Contains('\\') || name.Contains('/');
    }

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
        var path = Path.Combine(_serverEnv.GetDataRootPath(), "file");
        EnsureFolder(path);
        return Ok();
    }

    [HttpDelete("file")]
    public IActionResult DeleteFileFolder()
    {
        var path = Path.Combine(_serverEnv.GetDataRootPath(), "file");
        SafeDeleteFolder(path);
        return Ok();
    }

    [HttpHead("file/{fileName}")]
    [HttpGet("file/{fileName}")]
    public async Task<IActionResult> GetFileFromFolder(string fileName, CancellationToken token)
    {
        if (InvalidFileName(fileName))
        {
            return BadRequest();
        }

        try
        {
            var path = await _historyService.GetRecentTransferFile(HistoryService.HARD_CODED_USER_ID, fileName, token);
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
        if (InvalidFileName(fileName))
        {
            return BadRequest();
        }
        var folder = Path.Combine(_serverEnv.GetDataRootPath(), "file");
        EnsureFolder(folder);
        var path = Path.Combine(folder, fileName);
        using var fs = new FileStream(path, FileMode.Create);
        await Request.Body.CopyToAsync(fs);

        _cache.Remove("SyncClipboard.json");
        return Ok();
    }

    [HttpGet("SyncClipboard.json")]
    public async Task<ActionResult<ClipboardProfileDTO>> GetSyncProfile()
    {
        var path = Path.Combine(_serverEnv.GetDataRootPath(), "SyncClipboard.json");
        var cacheKey = path;
        if (_cache.TryGetValue(cacheKey, out ClipboardProfileDTO? cached))
        {
            return Ok(cached);
        }
        try
        {
            if (!System.IO.File.Exists(path))
            {
                return Ok(new ClipboardProfileDTO());
            }
            var text = await System.IO.File.ReadAllTextAsync(path);
            var dto = JsonSerializer.Deserialize<ClipboardProfileDTO>(text) ?? new ClipboardProfileDTO();
            _cache.Set(cacheKey, dto);
            return Ok(dto);
        }
        catch
        {
            return Ok(new ClipboardProfileDTO());
        }
    }

    [HttpPut("SyncClipboard.json")]
    public async Task<IActionResult> PutSyncProfile([FromBody] ClipboardProfileDTO profileDto, CancellationToken token)
    {
        var persistentDir = _serverEnv.GetPersistentDir();
        var profile = ClipboardProfileDTO.CreateProfile(profileDto);
        if (string.IsNullOrEmpty(profileDto.File) is false)
        {
            var fileName = Path.GetFileName(profileDto.File);
            var previousDataPath = Path.Combine(_serverEnv.GetDataRootPath(), "file", fileName);
            if (!System.IO.File.Exists(previousDataPath))
            {
                return BadRequest("Data file not found on server.");
            }

            try
            {
                await profile.SetAndMoveTransferData(persistentDir, previousDataPath, token);
            }
            catch when (!token.IsCancellationRequested)
            {
                return BadRequest("Data file is invalid.");
            }
        }

        await _historyService.AddProfile(HistoryService.HARD_CODED_USER_ID, profile, token);
        await SaveAndNotifyCurrentProfile(profileDto, token);

        return Ok();
    }

    [HttpPatch("api/current")]
    public async Task<IActionResult> SetCurrent([FromQuery] ProfileType type, [FromQuery] string hash, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return BadRequest("Hash cannot be empty");
        }

        var profile = await _historyService.GetProfileAsync(HistoryService.HARD_CODED_USER_ID, type, hash, token);
        if (profile == null)
        {
            return NotFound("Profile not found in history");
        }

        var profileDto = await profile.ToDto(token);
        await SaveAndNotifyCurrentProfile(profileDto, token);

        return Ok();
    }

    [HttpGet("")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult GetRoot()
    {
        return Ok("Server is running.");
    }

    private async Task SaveAndNotifyCurrentProfile(ClipboardProfileDTO profileDto, CancellationToken token)
    {
        var path = Path.Combine(_serverEnv.GetDataRootPath(), "SyncClipboard.json");
        _cache.Set(path, profileDto);
        var text = JsonSerializer.Serialize(profileDto);
        await System.IO.File.WriteAllTextAsync(path, text, token);
        await _hubContext.Clients.All.SendAsync(SignalRConstants.RemoteProfileChangedMethod, profileDto, token);
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