using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SyncClipboard.Server.Core.Hubs;
using Microsoft.AspNetCore.StaticFiles;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using SyncClipboard.Server.Core.Services.History;
using SyncClipboard.Server.Core.Services;

namespace SyncClipboard.Server.Core.Controllers;

[ApiController]
[Authorize]
public class SyncClipboardController(
    IHubContext<SyncClipboardHub, ISyncClipboardClient> _hubContext,
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
        await SaveAndNotifyCurrentProfile(profile, token);

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

        await SaveAndNotifyCurrentProfile(profile, token);

        return Ok();
    }

    [HttpGet("api/current")]
    public async Task<ActionResult<ProfileDto>> GetCurrent(CancellationToken token)
    {
        var profilePath = Path.Combine(_serverEnv.GetDataRootPath(), "current.json");
        var cacheKey = profilePath;

        if (_cache.TryGetValue(cacheKey, out ProfileDto? cachedProfile))
        {
            return Ok(cachedProfile);
        }

        if (!System.IO.File.Exists(profilePath))
        {
            var dto = await new TextProfile(string.Empty).ToProfileDto(token);
            _cache.Set(cacheKey, dto);
            return Ok(dto);
        }

        try
        {
            var text = await System.IO.File.ReadAllTextAsync(profilePath, token);
            cachedProfile = JsonSerializer.Deserialize<ProfileDto>(text) ?? new ProfileDto();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return BadRequest($"Failed to read current profile: {ex.Message}");
        }

        _cache.Set(cacheKey, cachedProfile);
        return Ok(cachedProfile);
    }

    [HttpGet("")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult GetRoot()
    {
        return Ok("Server is running.");
    }

    private async Task SaveAndNotifyCurrentProfile(Profile profile, CancellationToken token)
    {
        var clipboardProfileDto = await profile.ToDto(token);
        var dataRoot = _serverEnv.GetDataRootPath();

        var clipboardPath = Path.Combine(dataRoot, "SyncClipboard.json");
        _cache.Set(clipboardPath, clipboardProfileDto);
        var clipboardText = JsonSerializer.Serialize(clipboardProfileDto);
        await System.IO.File.WriteAllTextAsync(clipboardPath, clipboardText, token);

        var profileDto = await profile.ToProfileDto(token);
        var profilePath = Path.Combine(dataRoot, "current.json");
        _cache.Set(profilePath, profileDto);
        var profileText = JsonSerializer.Serialize(profileDto);
        await System.IO.File.WriteAllTextAsync(profilePath, profileText, token);

        await _hubContext.Clients.All.RemoteProfileChanged(profileDto);
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