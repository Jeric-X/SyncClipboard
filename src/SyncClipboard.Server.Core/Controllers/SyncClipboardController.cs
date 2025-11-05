using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SyncClipboard.Server.Core.Hubs;
using Microsoft.AspNetCore.StaticFiles;
using System.Text.Json;
using SyncClipboard.Server.Core.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using SyncClipboard.Server.Core.Services.History;

namespace SyncClipboard.Server.Core.Controllers;

[ApiController]
[Authorize]
public class SyncClipboardController(
    IHubContext<SyncClipboardHub> _hubContext,
    IWebHostEnvironment _env,
    IMemoryCache _cache,
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
        var path = Path.Combine(_env.WebRootPath, "file");
        EnsureFolder(path);
        return Ok();
    }

    [HttpDelete("file")]
    public IActionResult DeleteFileFolder()
    {
        var path = Path.Combine(_env.WebRootPath, "file");
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
        var folder = Path.Combine(_env.WebRootPath, "file");
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
        var path = Path.Combine(_env.WebRootPath, "SyncClipboard.json");
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
        var path = Path.Combine(_env.WebRootPath, "SyncClipboard.json");
        _cache.Set(path, profileDto);
        var text = JsonSerializer.Serialize(profileDto);
        var profile = ClipboardProfileDTO.CreateProfile(profileDto);
        if (profile is FileProfile fileProfile)
        {
            var dataPath = Path.Combine(_env.WebRootPath, "file", fileProfile.FileName);
            if (!System.IO.File.Exists(dataPath))
            {
                return BadRequest("Data file not found on server.");
            }
            var historyPath = Path.Combine(await _historyService.GetProfileDataFolder(profile, token), fileProfile.FileName);

            try
            {
                System.IO.File.Move(dataPath, historyPath, true);
                await fileProfile.SetTranseferData(historyPath, token);
            }
            catch when (!token.IsCancellationRequested)
            {
                return BadRequest("Data file is invalid.");
            }
        }

        List<Task> tasks = [
            _historyService.AddProfile(HistoryService.HARD_CODED_USER_ID, profile, token),
            System.IO.File.WriteAllTextAsync(path, text, token),
            _hubContext.Clients.All.SendAsync(SignalRConstants.RemoteProfileChangedMethod, profileDto, token)
        ];

        await Task.WhenAll(tasks);
        return Ok();
    }

    [HttpGet("{name}")]
    public async Task<IActionResult> GetFileRoot(string name)
    {
        if (InvalidFileName(name))
        {
            return BadRequest();
        }
        var path = Path.Combine(_env.WebRootPath, name);
        return await GetFileInternal(path);
    }

    [HttpPut("{name}")]
    public async Task<IActionResult> PutFileRoot(string name)
    {
        if (InvalidFileName(name))
        {
            return BadRequest();
        }
        var path = Path.Combine(_env.WebRootPath, name);
        using var fs = new FileStream(path, FileMode.Create);
        await Request.Body.CopyToAsync(fs);
        _cache.Remove(Path.Combine(_env.WebRootPath, "SyncClipboard.json"));
        return Ok();
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