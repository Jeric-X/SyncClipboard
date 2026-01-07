using SyncClipboard.Server.Core.Models;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Interfaces;

/// <summary>
/// Interface for syncing clipboard history with the server.
/// </summary>
public interface IOfficialSyncServer
{
    /// <summary>
    /// Event triggered when a history record changes on the server.
    /// </summary>
    event Action<HistoryRecordDto>? HistoryChanged;

    /// <summary>
    /// Gets a specific history record by profileId (format: "Type-Hash").
    /// </summary>
    /// <param name="profileId">The profile identifier in format "Type-Hash".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The history record DTO if found, null otherwise.</returns>
    Task<HistoryRecordDto?> GetHistoryByProfileIdAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the history records with optional pagination and time-based filtering.
    /// </summary>
    /// <param name="page">Page index starting from 1 (default 1). Page size is fixed to 50 (max 50).</param>
    /// <param name="before">DateTime (UTC). Only records with CreateTime/LastAccessed &lt; before will be returned.</param>
    /// <param name="after">DateTime (UTC). Only records with CreateTime/LastAccessed &gt;= after will be returned.</param>
    /// <param name="modifiedAfter">DateTime (UTC). Only records with LastModified &gt;= modifiedAfter will be returned.</param>
    /// <param name="types">Profile types filter (flags). Default All.</param>
    /// <param name="searchText">Optional search text to match text content.</param>
    /// <returns>A collection of history records matching the filter criteria.</returns>
    Task<IEnumerable<HistoryRecordDto>> GetHistoryAsync(int page = 1, DateTimeOffset? before = null, DateTimeOffset? after = null, DateTimeOffset? modifiedAfter = null, ProfileTypeFilter types = ProfileTypeFilter.All, string? searchText = null, bool? starred = null, bool sortByLastAccessed = false);

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
        HistoryRecordDto dto,
        string? filePath = null,
        IProgress<HttpDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取服务器当前时间
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务器当前时间</returns>
    Task<DateTimeOffset> GetServerTimeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取服务器版本
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务器版本字符串</returns>
    Task<string> GetVersionAsync(CancellationToken cancellationToken = default);
}
