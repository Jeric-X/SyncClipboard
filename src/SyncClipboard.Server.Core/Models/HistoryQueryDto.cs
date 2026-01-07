namespace SyncClipboard.Server.Core.Models;

/// <summary>
/// Query parameters for history records retrieval.
/// </summary>
public class HistoryQueryDto
{
    /// <summary>
    /// Page index starting from 1 (default 1). Page size is fixed to 50 (max 50).
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// DateTime (UTC). Only records with CreateTime/LastAccessed &lt; before will be returned.
    /// </summary>
    public DateTimeOffset? Before { get; set; }

    /// <summary>
    /// DateTime (UTC). Only records with CreateTime/LastAccessed >= after will be returned.
    /// </summary>
    public DateTimeOffset? After { get; set; }

    /// <summary>
    /// DateTime (UTC). Only records with LastModified >= modifiedAfter will be returned.
    /// </summary>
    public DateTimeOffset? ModifiedAfter { get; set; }

    /// <summary>
    /// Profile types filter (flags). Default All.
    /// </summary>
    public ProfileTypeFilter Types { get; set; } = ProfileTypeFilter.All;

    /// <summary>
    /// Optional search text to match text content (case-insensitive).
    /// </summary>
    public string? SearchText { get; set; }

    /// <summary>
    /// Filter by starred status. Null means no filter.
    /// </summary>
    public bool? Starred { get; set; }

    /// <summary>
    /// Whether to sort by LastAccessed instead of CreateTime.
    /// </summary>
    public bool SortByLastAccessed { get; set; }
}
