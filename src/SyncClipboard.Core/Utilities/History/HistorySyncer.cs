using SyncClipboard.Core.Models;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Exceptions;

namespace SyncClipboard.Core.Utilities.History;

public class HistorySyncer
{
    private readonly HistoryManager _historyManager;
    private readonly RemoteClipboardServerFactory _remoteServerFactory;
    private readonly ILogger _logger;

    public HistorySyncer(
        HistoryManager historyManager,
        RemoteClipboardServerFactory remoteServerFactory,
        ILogger logger)
    {
        _historyManager = historyManager;
        _remoteServerFactory = remoteServerFactory;
        _logger = logger;

        // 订阅 HistoryManager 的事件：更新与删除（软删除）均可能需要同步
        _historyManager.HistoryUpdated += OnHistoryUpdated;
        _historyManager.HistoryRemoved += OnHistoryRemoved;
    }

    public async Task<List<HistoryRecord>> SyncRangeAsync(
        DateTime? before = null,
        DateTime? after = null,
        ProfileTypeFilter types = ProfileTypeFilter.All,
        string? searchText = null,
        bool? starred = null,
        int pageLimit = int.MaxValue,
        CancellationToken token = default)
    {
        if (_remoteServerFactory.Current is not IOfficialSyncServer remoteServer)
        {
            return [];
        }

        var remoteRecords = await FetchRemoteRangeAsync(
            remoteServer,
            before,
            after,
            types,
            searchText,
            starred,
            pageLimit,
            null,
            token);

        var addedRecords = await _historyManager.SyncRemoteHistoryAsync(remoteRecords, token);
        await DetectOrphanDataAsync(before, after, remoteRecords, types, searchText, starred, token);
        await PushLocalRangeAsync(before, after, types, searchText, starred, token);
        return addedRecords;
    }

    public async Task SyncAllAsync(DateTime? modifiedAfter, CancellationToken token = default)
    {
        if (_remoteServerFactory.Current is not IOfficialSyncServer remoteServer)
        {
            return;
        }

        var remoteRecords = await FetchRemoteRangeAsync(
            remoteServer,
            null,
            null,
            ProfileTypeFilter.All,
            null,
            null,
            int.MaxValue,
            modifiedAfter,
            token);

        await _historyManager.SyncRemoteHistoryAsync(remoteRecords, token);
        await PushLocalRangeAsync(null, null, ProfileTypeFilter.All, null, null, token);
    }

    /// <summary>
    /// 遍历所有本地历史记录，将 SyncStatus == NeedSync 的记录更新到服务器。
    /// 如果服务器返回 409 冲突，则使用服务器返回的记录内容更新本地。
    /// </summary>
    public async Task SyncNeedUpdateAsync(CancellationToken token = default)
    {
        List<HistoryRecord> records;
        try
        {
            records = await _historyManager.GetHistory();
        }
        catch (Exception ex)
        {
            _logger.Write("HistorySyncer", $"获取本地历史记录失败: {ex.Message}");
            return;
        }

        var needSync = records.Where(r => r.SyncStatus == HistorySyncStatus.NeedSync).ToList();
        if (needSync.Count == 0)
        {
            return;
        }

        foreach (var record in needSync)
        {
            await SyncOneAsync(record, token);
        }
    }

    /// <summary>
    /// 同步单条历史记录到服务器，处理并发冲突与本地回写。
    /// </summary>
    /// <param name="record">本地历史记录</param>
    /// <param name="token">取消令牌</param>
    /// <returns>返回是否已将本地状态更新为 Synced</returns>
    public async Task SyncOneAsync(HistoryRecord record, CancellationToken token = default)
    {
        if (_remoteServerFactory.Current is not IOfficialSyncServer remoteServer)
        {
            _logger.Write("HistorySyncer", "当前远程服务器不支持历史记录同步");
            return;
        }

        try
        {
            var dto = new HistoryRecordUpdateDto
            {
                Starred = record.Stared,
                Pinned = record.Pinned,
                IsDelete = record.IsDeleted ? true : null,
                Version = record.Version,
                LastModified = new DateTimeOffset(record.LastModified.ToUniversalTime(), TimeSpan.Zero)
            };
            await remoteServer.UpdateHistoryAsync(record.Type, record.Hash, dto, token);
            record.SyncStatus = HistorySyncStatus.Synced;
            await _historyManager.PersistServerSyncedAsync(record, token);
        }
        catch (RemoteHistoryConflictException ex)
        {
            if (ex.ServerRecord != null)
            {
                record.ApplyFromServerUpdateDto(ex.ServerRecord);
                await _historyManager.PersistServerSyncedAsync(record, token);
            }
            _logger.Write("HistorySyncer", $"并发冲突，已回写服务器版本[{record.Hash}]");
        }
        catch (RemoteHistoryNotFoundException)
        {
            record.SyncStatus = HistorySyncStatus.LocalOnly;
            await _historyManager.PersistServerSyncedAsync(record, token);
        }
        catch (Exception ex)
        {
            _logger.Write("HistorySyncer", $"同步记录异常[{record.Hash}]: {ex.Message}");
            return;
        }
    }

    /// <summary>
    /// 从服务器拉取指定时间范围内的所有历史记录(分页获取)
    /// </summary>
    private async Task<List<HistoryRecordDto>> FetchRemoteRangeAsync(
        IOfficialSyncServer remoteServer,
        DateTime? before,
        DateTime? after,
        ProfileTypeFilter types,
        string? searchText,
        bool? starred,
        int pageLimit,
        DateTime? modifiedAfter,
        CancellationToken token)
    {
        var allRecords = new List<HistoryRecordDto>();
        var page = 1;

        long? beforeMs = before.HasValue ? ToUnixMilliseconds(before.Value) : null;
        long? afterMs = after.HasValue ? ToUnixMilliseconds(after.Value) : null;
        long? modifiedAfterMs = modifiedAfter.HasValue ? ToUnixMilliseconds(modifiedAfter.Value) : null;

        try
        {
            while (page <= pageLimit && !token.IsCancellationRequested)
            {
                var pageRecords = await remoteServer.GetHistoryAsync(
                    page: page,
                    before: beforeMs,
                    after: afterMs,
                    modifiedAfter: modifiedAfterMs,
                    types: types,
                    searchText: searchText,
                    starred: starred);

                if (!pageRecords.Any())
                {
                    break;
                }

                allRecords.AddRange(pageRecords);
                page++;

                // 如果返回少于一页的数据，说明已到末尾
                if (pageRecords.Count() < 50) // 假设页大小为50
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            _logger.Write("HistorySyncer", $"从服务器拉取范围数据失败: {ex.Message}");
        }

        return allRecords;
    }

    /// <summary>
    /// 检测并处理孤儿数据：本地标记为 Synced/ServerOnly 但服务器不存在的记录
    /// </summary>
    private async Task DetectOrphanDataAsync(
        DateTime? before,
        DateTime? after,
        List<HistoryRecordDto> remoteRecords,
        ProfileTypeFilter types,
        string? searchText,
        bool? starred,
        CancellationToken token)
    {
        try
        {
            // 获取本地该范围内标记为 Synced 或 ServerOnly 的记录
            var localRecords = await _historyManager.GetHistoryAsync(
                types,
                starred,
                before,
                int.MaxValue,
                searchText,
                token);

            var localSyncedOrServerOnly = localRecords
                .Where(r => r.SyncStatus == HistorySyncStatus.Synced || r.IsLocalFileReady is false)
                .Where(r => !after.HasValue || r.Timestamp >= after.Value);

            // 构建服务器记录的标识集合
            var remoteIds = remoteRecords.Select(r => $"{r.Type}-{r.Hash}").ToHashSet();

            // 找出孤儿数据：本地认为已同步但服务器不存在
            foreach (var localRecord in localSyncedOrServerOnly)
            {
                var localId = $"{localRecord.Type}-{localRecord.Hash}";
                if (remoteIds.Contains(localId))
                {
                    continue;
                }
                if (localRecord.IsLocalFileReady is false)
                {
                    await _historyManager.RemoveHistory(localRecord, token);
                    continue;
                }
                // 孤儿数据：服务器已删除，修改为 LocalOnly
                _logger.Write("HistorySyncer", $"检测到孤儿数据 [{localId}]，标记为 LocalOnly");
                localRecord.SyncStatus = HistorySyncStatus.LocalOnly;
                await _historyManager.PersistServerSyncedAsync(localRecord, token);
            }
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            _logger.Write("HistorySyncer", $"孤儿数据检测失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 推送本地范围内需要同步的记录到服务器
    /// </summary>
    private async Task PushLocalRangeAsync(
        DateTime? before,
        DateTime? after,
        ProfileTypeFilter types,
        string? searchText,
        bool? starred,
        CancellationToken token)
    {
        try
        {
            var localRecords = await _historyManager.GetHistoryAsync(
                types,
                starred,
                before,
                int.MaxValue,
                searchText,
                token);

            var needSync = localRecords
                .Where(r => r.SyncStatus == HistorySyncStatus.NeedSync
                           && (!after.HasValue || r.Timestamp >= after.Value))
                .ToList();

            foreach (var record in needSync)
            {
                if (token.IsCancellationRequested)
                    break;

                await SyncOneAsync(record, token);
            }
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            _logger.Write("HistorySyncer", $"推送本地范围数据失败: {ex.Message}");
        }
    }

    private static long ToUnixMilliseconds(DateTime time)
    {
        if (time.Kind == DateTimeKind.Unspecified)
        {
            time = DateTime.SpecifyKind(time, DateTimeKind.Utc);
        }
        return new DateTimeOffset(time).ToUnixTimeMilliseconds();
    }

    private async void OnHistoryUpdated(HistoryRecord record)
    {
        try
        {
            if (record.SyncStatus == HistorySyncStatus.NeedSync)
            {
                await SyncOneAsync(record, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.Write("HistorySyncer", $"事件同步失败[{record.Hash}]: {ex.Message}");
        }
    }

    private async void OnHistoryRemoved(HistoryRecord record)
    {
        try
        {
            if (record.IsDeleted && record.SyncStatus == HistorySyncStatus.NeedSync)
            {
                await SyncOneAsync(record, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.Write("HistorySyncer", $"删除事件同步失败[{record.Hash}]: {ex.Message}");
        }
    }
}

