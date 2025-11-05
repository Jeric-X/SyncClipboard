using SyncClipboard.Server.Core.Models;

namespace SyncClipboard.Core.Interfaces;

/// <summary>
/// Interface for syncing clipboard history with the server.
/// </summary>
public interface IHistorySyncServer
{
    /// <summary>
    /// Gets the history records with optional pagination and time-based filtering.
    /// </summary>
    /// <param name="page">Page index starting from 1 (default 1). Page size is fixed to 50 (max 50).</param>
    /// <param name="before">Unix timestamp in milliseconds (UTC). Only records with CreateTime &lt; before will be returned.</param>
    /// <param name="after">Unix timestamp in milliseconds (UTC). Only records with CreateTime &gt; after will be returned.</param>
    /// <param name="cursorProfileId">Optional string cursor representing a profile id for cursor-based pagination.</param>
    /// <param name="types">Profile types filter (flags). Default All.</param>
    /// <param name="searchText">Optional search text to match text content.</param>
    /// <returns>A collection of history records matching the filter criteria.</returns>
    Task<IEnumerable<HistoryRecordDto>> GetHistoryAsync(int page = 1, long? before = null, long? after = null, string? cursorProfileId = null, ProfileTypeFilter types = ProfileTypeFilter.All, string? searchText = null, bool? starred = null);
}
