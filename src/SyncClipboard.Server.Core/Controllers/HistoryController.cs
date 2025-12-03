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

    // GET api/history/{profileId}
    // Returns the metadata of a specific history record by profileId (format: "Type-Hash")
    [HttpGet("{profileId}")]
    public async Task<IActionResult> Get(string profileId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return BadRequest("profileId is required");
        }

        if (!Profile.ParseProfileId(profileId, out var type, out var hash) || string.IsNullOrEmpty(hash))
        {
            return BadRequest("Invalid profileId format. Expected format: 'Type-Hash'");
        }

        var rec = await _historyService.GetByTypeAndHashAsync(HARD_CODED_USER_ID, type, hash, token);
        if (rec == null)
        {
            return NotFound();
        }

        return Ok(rec);
    }

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
        var fileName = Path.GetFileName(path);
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
    //   after:  Unix timestamp in milliseconds (UTC). Only records with CreateTime >= after will be returned.
    //   types: ProfileTypeFilter flag enum (default All). Example: types=Text,Image
    //   q: optional search text (matches Text field, case-insensitive)
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? page,
        [FromQuery] long? before = null,
        [FromQuery] long? after = null,
        [FromQuery] ProfileTypeFilter? types = ProfileTypeFilter.All,
        [FromQuery(Name = "q")] string? searchText = null,
        [FromQuery] bool? starred = null)
    {
        page ??= 1;
        types ??= ProfileTypeFilter.All;
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
            page.Value,
            PAGE_SIZE,
            beforeDt,
            afterDt,
            types.Value,
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
    /// PUT api/history
    /// 使用 query 参数传递元数据，使用 body 传递文件数据（application/octet-stream）。
    /// Query 参数：Hash, Type, CreateTime, LastModified, Starred, Pinned, Version, IsDeleted, Text, Size
    /// 若不存在则创建；若已存在则返回 409 并携带服务器快照用于客户端决策。
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> Put(
        [FromQuery] string hash,
        [FromQuery] ProfileType type,
        [FromQuery] DateTimeOffset? createTime,
        [FromQuery] DateTimeOffset? lastModified,
        [FromQuery] bool starred = false,
        [FromQuery] bool pinned = false,
        [FromQuery] int version = 0,
        [FromQuery] bool isDeleted = false,
        [FromQuery] string? text = null,
        [FromQuery] long size = 0,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(hash)) return BadRequest("Hash is required");
        if (type == ProfileType.None) return BadRequest("Type is required");

        var dto = new HistoryRecordDto
        {
            Hash = hash,
            Type = type,
            CreateTime = createTime ?? DateTimeOffset.UtcNow,
            LastModified = lastModified ?? DateTimeOffset.UtcNow,
            Starred = starred,
            Pinned = pinned,
            Version = version,
            IsDeleted = isDeleted,
            Text = text ?? string.Empty,
            Size = size
        };

        // 从 body 读取文件流
        Stream? fileStream = null;
        if (Request.ContentLength.HasValue && Request.ContentLength.Value > 0)
        {
            fileStream = Request.Body;
        }

        var result = await _historyService.CreateIfNotExistsAsync(
            HARD_CODED_USER_ID, dto, fileStream, token);
        if (result.Created)
        {
            return CreatedAtAction(nameof(GetTransferData), new { profileId = $"{dto.Type}-{dto.Hash}" }, null);
        }
        // 将 HistoryRecordDto 转换为 HistoryRecordUpdateDto
        HistoryRecordUpdateDto? updateDto = null;
        if (result.Server != null)
        {
            updateDto = new HistoryRecordUpdateDto
            {
                Starred = result.Server.Starred,
                Pinned = result.Server.Pinned,
                LastModified = result.Server.LastModified,
                Version = result.Server.Version,
                IsDelete = result.Server.IsDeleted
            };
        }
        return Conflict(updateDto);
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

    // DELETE api/history/clear
    // 清除当前用户的所有历史记录及其相关数据文件。
    [HttpDelete("clear")]
    public async Task<IActionResult> ClearAll(CancellationToken token)
    {
        var deleted = await _historyService.ClearAllAsync(HARD_CODED_USER_ID, token);
        return Ok(new { deleted });
    }
}
