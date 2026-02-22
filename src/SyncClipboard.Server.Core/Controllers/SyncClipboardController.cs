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
[Tags("SyncClipboard")]
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
    public async Task<ActionResult<ProfileDto>> GetSyncProfile(CancellationToken token)
    {
        var profilePath = Path.Combine(_serverEnv.GetDataRootPath(), "SyncClipboard.json");
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
            var dto = await new TextProfile(string.Empty).ToProfileDto(token);
            _cache.Set(cacheKey, dto);
            return Ok(dto);
        }

        _cache.Set(cacheKey, cachedProfile);
        return Ok(cachedProfile);
    }

    [HttpPut("SyncClipboard.json")]
    public async Task<IActionResult> PutSyncProfile([FromBody] ProfileDto dto, CancellationToken token)
    {
        if (dto is null)
        {
            return BadRequest("dto cannot be null");
        }

        if (!string.IsNullOrWhiteSpace(dto.Hash))
        {
            var profile = await _historyService.GetExistingProfileAsync(
                HistoryService.HARD_CODED_USER_ID, dto.Type, dto.Hash, token);

            if (profile != null)
            {
                await SaveAndNotifyCurrentProfile(profile, token);
                return Ok();
            }
        }

        return await CreateAndSaveNewProfile(dto, token);
    }

    [HttpGet("")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult GetRoot()
    {
        return Ok("Server is running.");
    }

    private async Task<IActionResult> CreateAndSaveNewProfile(ProfileDto dto, CancellationToken token)
    {
        var newProfile = Profile.Create(dto);

        if (dto.HasData)
        {
            if (string.IsNullOrEmpty(dto.DataName))
            {
                return BadRequest("DataName cannot be null or empty when HasData is true");
            }

            var fileName = Path.GetFileName(dto.DataName);
            var previousDataPath = Path.Combine(_serverEnv.GetDataRootPath(), "file", fileName);
            if (!System.IO.File.Exists(previousDataPath))
            {
                return NotFound("Transfer data file not found");
            }

            var persistentDir = _serverEnv.GetPersistentDir();
            try
            {
                await newProfile.SetAndMoveTransferData(persistentDir, previousDataPath, token);
            }
            catch when (!token.IsCancellationRequested)
            {
                return BadRequest("Hash is not match data.");
            }
        }

        await _historyService.AddProfile(HistoryService.HARD_CODED_USER_ID, newProfile, token);
        await SaveAndNotifyCurrentProfile(newProfile, token);
        return Ok();
    }

    private async Task SaveAndNotifyCurrentProfile(Profile profile, CancellationToken token)
    {
        var profileDto = await profile.ToProfileDto(token);
        var dataRoot = _serverEnv.GetDataRootPath();

        var profilePath = Path.Combine(dataRoot, "SyncClipboard.json");
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