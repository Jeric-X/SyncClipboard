using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using SyncClipboard.Server.Core.Attributes;
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
    //   modifiedAfter: Unix timestamp in milliseconds (UTC). Only records with LastModified >= modifiedAfter will be returned.
    //   types: ProfileTypeFilter flag enum (default All). Example: types=Text,Image
    //   q: optional search text (matches Text field, case-insensitive)
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? page,
        [FromQuery] long? before = null,
        [FromQuery] long? after = null,
        [FromQuery] long? modifiedAfter = null,
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

        DateTime? modifiedAfterDt = null;
        if (modifiedAfter.HasValue)
        {
            try
            {
                modifiedAfterDt = DateTimeOffset.FromUnixTimeMilliseconds(modifiedAfter.Value).UtcDateTime;
            }
            catch (ArgumentOutOfRangeException)
            {
                return BadRequest("modifiedAfter must be a valid Unix timestamp in milliseconds");
            }
        }

        var list = await _historyService.GetListAsync(
            HARD_CODED_USER_ID,
            page.Value,
            PAGE_SIZE,
            beforeDt,
            afterDt,
            types.Value,
            searchText,
            starred,
            modifiedAfterDt);
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
    /// POST api/history
    /// 使用 multipart/form-data 流式传输文件和元数据。
    /// Form 字段：hash, type, createTime, lastModified, starred, pinned, version, isDeleted, text, size, data
    /// data 字段为可选的二进制文件数据。
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestFormLimits(ValueLengthLimit = int.MaxValue, MultipartBodyLengthLimit = long.MaxValue)]
    [DisableFormValueModelBinding]
    public async Task<IActionResult> Put(CancellationToken token = default)
    {
        var boundary = HeaderUtilities.RemoveQuotes(MediaTypeHeaderValue.Parse(Request.ContentType).Boundary).Value;
        if (string.IsNullOrEmpty(boundary))
        {
            return BadRequest("Invalid or missing multipart/form-data boundary");
        }

        try
        {
            var reader = new MultipartReader(boundary, Request.Body, 10 * 1024);
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Stream? fileStream = null;
            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(token)) != null)
            {
                var (hasData, dataStream) = await TryHandleSectionAsync(section, metadata, token);
                if (hasData)
                {
                    fileStream = dataStream;
                    break; // 文件流必须是最后一个部分
                }
            }

            var dto = ParseHistoryRecord(metadata);

            var serverDto = await _historyService.AddRecordDto(HARD_CODED_USER_ID, dto, fileStream, token);
            return Ok(serverDto);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex) when (token.IsCancellationRequested == false)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    private static HistoryRecordDto ParseHistoryRecord(Dictionary<string, string> metadata)
    {
        if (!metadata.TryGetValue("hash", out var hash) || string.IsNullOrWhiteSpace(hash))
            throw new ArgumentException("Hash is required");

        if (!metadata.TryGetValue("type", out var typeStr)
            || string.IsNullOrWhiteSpace(typeStr)
            || !Enum.TryParse<ProfileType>(typeStr, true, out var type))
        {
            throw new ArgumentException("Type is required");
        }
        if (type == ProfileType.None)
            throw new ArgumentException("Type is required");

        DateTimeOffset? createTime = null;
        if (metadata.TryGetValue("createTime", out var ctStr) && !string.IsNullOrWhiteSpace(ctStr))
        {
            DateTimeOffset.TryParse(ctStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var ct);
            createTime = ct;
        }

        DateTimeOffset? lastModified = null;
        if (metadata.TryGetValue("lastModified", out var lmStr) && !string.IsNullOrWhiteSpace(lmStr))
        {
            DateTimeOffset.TryParse(lmStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var lm);
            lastModified = lm;
        }

        bool starred = metadata.TryGetValue("starred", out var starStr) && bool.TryParse(starStr, out var star) && star;

        bool pinned = metadata.TryGetValue("pinned", out var pinStr) && bool.TryParse(pinStr, out var pin) && pin;

        int version = 0;
        if (metadata.TryGetValue("version", out var verStr) && int.TryParse(verStr, out var ver))
            version = ver;

        bool isDeleted = metadata.TryGetValue("isDeleted", out var delStr) && bool.TryParse(delStr, out var del) && del;

        string text = metadata.TryGetValue("text", out var textStr) ? textStr : string.Empty;

        long size = 0;
        if (metadata.TryGetValue("size", out var sizeStr) && long.TryParse(sizeStr, out var sz))
            size = sz;

        bool hasData = metadata.TryGetValue("hasData", out var hasDataStr) && bool.TryParse(hasDataStr, out var hasDataVal) && hasDataVal;

        return new HistoryRecordDto
        {
            Hash = hash,
            Type = type,
            CreateTime = createTime ?? DateTimeOffset.UtcNow,
            LastModified = lastModified ?? DateTimeOffset.UtcNow,
            Starred = starred,
            Pinned = pinned,
            Version = version,
            IsDeleted = isDeleted,
            Text = text,
            Size = size,
            HasData = hasData
        };
    }

    private static async Task<(bool HasData, Stream? DataStream)> TryHandleSectionAsync(
        MultipartSection section,
        Dictionary<string, string> metadata,
        CancellationToken token)
    {
        var hasContentDispositionHeader = ContentDispositionHeaderValue.TryParse(
            section.ContentDisposition, out var contentDisposition);

        if (!hasContentDispositionHeader || contentDisposition?.Name == null)
            return (false, null);

        var fieldName = contentDisposition.Name.HasValue ? contentDisposition.Name.Value.Trim('"') : null;
        if (string.IsNullOrEmpty(fieldName))
            return (false, null);

        if (fieldName == "data")
        {
            return (true, section.Body);
        }

        using var streamReader = new StreamReader(section.Body);
        var value = await streamReader.ReadToEndAsync(token);
        metadata[fieldName] = value;
        return (false, null);
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

        var (updated, server) = await _historyService.Update(
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
