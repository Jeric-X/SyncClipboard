using SyncClipboard.Server.Core.Models;
using SyncClipboard.Core.Models;

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

    /// <summary>
    /// Download transfer data file for a history record specified by profileId (Type-Hash) to localPath.
    /// </summary>
    Task DownloadHistoryDataAsync(string profileId, string localPath, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update history record to server with optimistic concurrency.
    /// - On 200 OK: completes without payload
    /// - On 409 Conflict: throws RemoteHistoryConflictException carrying server payload
    /// - On 404 NotFound: throws RemoteHistoryNotFoundException
    /// </summary>
    Task UpdateHistoryAsync(ProfileType type, string hash, HistoryRecordUpdateDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// 上传一个本地仅存在的记录（初次同步）。若服务器已存在则抛出冲突异常。
    /// </summary>
    /// 上传一个本地仅存在的记录（初次同步）。若服务器已存在则抛出冲突异常。
    /// createTime: 原始创建时间（记录的 Timestamp）
    /// filePath: 可选的本地传输文件路径（若需要）。
    Task UploadHistoryAsync(
        ProfileType type,
        string hash,
        HistoryRecordUpdateDto dto,
        DateTimeOffset createTime,
        string? filePath = null,
        IProgress<HttpDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
