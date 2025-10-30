using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Services.History;

namespace SyncClipboard.Server.Core.Controllers;

[ApiController]
[Route("api/[controller]")]
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

    // GET api/history/{type}
    // [HttpGet("{type}")]
    // public async Task<IActionResult> GetList(ProfileType type)
    // {
    //     var list = await _historyService.GetListAsync(HARD_CODED_USER_ID, type);
    //     return Ok(list);
    // }

    // GET api/history
    // Return all types
    // Query parameters:
    //   page: page index starting from 1 (default 1). Page size is fixed to 50 (max 50).
    //   before: Unix timestamp in milliseconds (UTC). Only records with CreateTime < before will be returned.
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] long? before = null)
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

        var list = await _historyService.GetListAsync(HARD_CODED_USER_ID, ProfileType.None, page, PAGE_SIZE, beforeDt);
        return Ok(list);
    }

    // PUT api/history/{type}/{hash}
    // Query parameter: fileName (optional)
    // Body is optional binary data stream to be stored/associated with the history record.
    // [HttpPut("{type}/{hash}")]
    // public async Task<IActionResult> Put(ProfileType type, string hash, [FromQuery] string? fileName)
    // {
    //     // Use provided hash from path

    //     // validate fileName if provided
    //     if (!string.IsNullOrEmpty(fileName) && IsInvalidFileName(fileName))
    //     {
    //         return BadRequest("file name contains invalid characters");
    //     }

    //     // Pass the raw request body stream to service for optional data storage
    //     Stream? dataStream = null;
    //     if (Request.Body != null && Request.ContentLength > 0)
    //     {
    //         dataStream = Request.Body;
    //     }

    //     await _historyService.SetWithDataAsync(HARD_CODED_USER_ID, hash, type, dataStream, fileName);
    //     return Ok();
    // }

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

    // PATCH api/history/{type}/{hash}
    // Update metadata fields for a history record.
    // [HttpPatch("{type}/{hash}")]
    // public async Task<IActionResult> Update(ProfileType type, string hash, [FromBody] HistoryRecordUpdateDto dto)
    // {
    //     if (dto == null) return BadRequest();

    //     var success = await _historyService.UpdateAsync(HARD_CODED_USER_ID, hash, type, dto);

    //     return success ? NoContent() : NotFound();
    // }
}
