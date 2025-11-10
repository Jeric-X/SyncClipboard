using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Services.History;

namespace SyncClipboard.Server.Core.Controllers;

[ApiController]
[Route("api/history")]
[Authorize]
public class HistoryController(HistoryService historyService) : ControllerBase
{
    private readonly HistoryService _historyService = historyService;
    // TODO: replace this hardcoded user id with actual user identification from authentication/claims
    private const string HARD_CODED_USER_ID = HistoryService.HARD_CODED_USER_ID;

    // GET api/history/{type}/{hash}
    // [HttpGet("{type}/{hash}")]
    // public async Task<IActionResult> Get(ProfileType type, string hash)
    // {
    //     var rec = await _historyService.GetAsync(HARD_CODED_USER_ID, hash, type);
    //     if (rec == null) return NotFound();
    //     return Ok(rec);
    // }

    // GET api/history/{profileId}/data
    // Returns the transfer data associated with a specific history profile.
    [HttpGet("{profileId}/data")]
    public async Task<IActionResult> GetTransferData(string profileId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return BadRequest("profileId is required");
        }

        var path = await _historyService.GetTransferDataFileByProfileId(HARD_CODED_USER_ID, profileId, token);
        if (string.IsNullOrEmpty(path))
        {
            return NotFound();
        }

        new FileExtensionContentTypeProvider().TryGetContentType(path, out string? contentType);
        var stream = System.IO.File.OpenRead(path);
        var fileName = System.IO.Path.GetFileName(path);
        return File(stream, contentType ?? "application/octet-stream", fileName);
    }

    // GET api/history/{type}
    // [HttpGet("{type}")]
    // public async Task<IActionResult> GetList(ProfileType type)
    // {
    //     var list = await _historyService.GetListAsync(HARD_CODED_USER_ID, type);
    //     return Ok(list);
    // }

    // GET api/history
    // Return list with optional filters
    // Query parameters:
    //   page: page index starting from 1 (default 1). Page size is fixed to 50 (max 50).
    //   before: Unix timestamp in milliseconds (UTC). Only records with CreateTime < before will be returned.
    //   after:  Unix timestamp in milliseconds (UTC). Only records with CreateTime > after will be returned.
    //   cursorProfileId: optional string cursor representing a profile id.
    //   types: ProfileTypeFilter flag enum (default All). Example: types=Text,Image
    //   q: optional search text (matches Text field, case-insensitive)
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] long? before = null,
        [FromQuery] long? after = null,
        [FromQuery] string? cursorProfileId = null,
        [FromQuery] ProfileTypeFilter types = ProfileTypeFilter.All,
        [FromQuery(Name = "q")] string? searchText = null,
        [FromQuery] bool? starred = null)
    {
        if (page < 1)
            return BadRequest("page must be >= 1");

        const int PAGE_SIZE = 50;

        DateTime? beforeDt = null;
        if (before.HasValue)
        {
            try
            {
                beforeDt = DateTimeOffset.FromUnixTimeMilliseconds(before.Value).UtcDateTime;
            }
            catch (ArgumentOutOfRangeException)
            {
                return BadRequest("before must be a valid Unix timestamp in milliseconds");
            }
        }

        DateTime? afterDt = null;
        if (after.HasValue)
        {
            try
            {
                afterDt = DateTimeOffset.FromUnixTimeMilliseconds(after.Value).UtcDateTime;
            }
            catch (ArgumentOutOfRangeException)
            {
                return BadRequest("after must be a valid Unix timestamp in milliseconds");
            }
        }

        if (beforeDt.HasValue && afterDt.HasValue && afterDt.Value >= beforeDt.Value)
        {
            return BadRequest("after must be less than before");
        }

        var list = await _historyService.GetListAsync(
            HARD_CODED_USER_ID,
            page,
            PAGE_SIZE,
            beforeDt,
            afterDt,
            cursorProfileId,
            types,
            searchText,
            starred);
        return Ok(list);
    }

    private static bool IsInvalidFileName(string fileName)
    {
        // Reject path traversal
        if (fileName.Contains("..")) return true;

        // Disallow directory separators explicitly
        if (fileName.Contains(Path.DirectorySeparatorChar)) return true;
        if (fileName.Contains(Path.AltDirectorySeparatorChar)) return true;

        // Disallow any OS-invalid filename characters
        var invalid = Path.GetInvalidFileNameChars();
        if (fileName.IndexOfAny(invalid) >= 0) return true;

        // Length limit (avoid extremely long names)
        if (fileName.Length > 255) return true;

        // Control characters
        foreach (var c in fileName)
        {
            if (char.IsControl(c)) return true;
        }

        return false;
    }

    /// <summary>
    /// PUT api/history/{type}/{hash}
    /// 使用 multipart/form-data 提交；支持可选的 TransferFile 文件。
    /// 其余元数据字段（stared/pinned/isDelete/version/lastModified/createTime）为必填。
    /// 若不存在则创建；若已存在则返回 409 冲突并携带服务器快照。
    /// </summary>
    [HttpPut("{type}/{hash}")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Put(
        ProfileType type,
        string hash,
        [FromForm] bool stared,
        [FromForm] bool pinned,
        [FromForm] bool isDelete,
        [FromForm] int version,
        [FromForm] DateTimeOffset lastModified,
        [FromForm] DateTimeOffset createTime,
        IFormFile? file,
        CancellationToken token)
    {
        // file 可为空；其余必填字段若缺失，模型绑定会使 ModelState 失败
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var dto = new HistoryRecordUpdateDto
        {
            Stared = stared,
            Pinned = pinned,
            IsDelete = isDelete,
            Version = version,
            LastModified = lastModified
        };

        var (created, server) = await _historyService.CreateIfNotExistsAsync(
            HARD_CODED_USER_ID, type, hash, dto, file, createTime, token);
        if (created)
        {
            return CreatedAtAction(nameof(GetTransferData), new { profileId = $"{type}-{hash}" }, null);
        }

        // 已存在：冲突，返回当前服务器端快照用于客户端决策
        return Conflict(server?.ToUpdateDto());
    }

    // PATCH api/history/{type}/{hash}
    // 根据时间戳和version综合判断是否更新
    [HttpPatch("{type}/{hash}")]
    public async Task<IActionResult> Update(ProfileType type, string hash, [FromBody] HistoryRecordUpdateDto dto, CancellationToken token)
    {
        if (dto is null)
        {
            return BadRequest();
        }

        var (updated, server) = await _historyService.UpdateWithConcurrencyAsync(
            HARD_CODED_USER_ID, type, hash, dto, token);

        if (updated is null)
        {
            return NotFound();
        }

        var payload = server?.ToUpdateDto();

        if (updated.Value)
        {
            return Ok(); // 成功时不返回 payload
        }

        return Conflict(payload);
    }
}
