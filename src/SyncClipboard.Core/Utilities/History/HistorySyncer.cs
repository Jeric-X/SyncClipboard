using SyncClipboard.Core.Models;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Exceptions;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Clipboard;
using Microsoft.Extensions.DependencyInjection;

namespace SyncClipboard.Core.Utilities.History;

public class HistorySyncer
{
    private readonly HistoryManager _historyManager;
    private readonly RemoteClipboardServerFactory _remoteServerFactory;
    private readonly ILogger _logger;
    private readonly HistoryTransferQueue _historyTransferQueue;
    private readonly ConfigBase _runtimeConfig;
    private readonly ConfigManager _configManager;

    public HistorySyncer(
        HistoryManager historyManager,
        RemoteClipboardServerFactory remoteServerFactory,
        ILogger logger,
        HistoryTransferQueue historyTransferQueue,
        ConfigManager configManager,
        [FromKeyedServices(Env.RuntimeConfigName)] ConfigBase runtimeConfig)
    {
        _historyManager = historyManager;
        _remoteServerFactory = remoteServerFactory;
        _logger = logger;
        _historyTransferQueue = historyTransferQueue;
        _configManager = configManager;
        _runtimeConfig = runtimeConfig;

        // 订阅 HistoryManager 的事件：更新与删除（软删除）均可能需要同步
        _historyManager.HistoryAdded += OnHistoryAdded;
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
        bool sortByLastAccessed = false,
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
            sortByLastAccessed,
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
            false,
            token);

        await _historyManager.SyncRemoteHistoryAsync(remoteRecords, token);
        if (modifiedAfter is null)
        {
            await DetectOrphanDataAsync(null, null, remoteRecords, ProfileTypeFilter.All, null, null, token);
        }
        await PushLocalRangeAsync(null, null, ProfileTypeFilter.All, null, null, token);
        await SyncPendingHistoryDataAsync(token);
        await _historyTransferQueue.WaitAllTasks(token);
    }

    // 关闭历史记录同步功能后，将所有远程历史记录标记为本地，不完整的记录删除
    public async Task RemoveRemoteHistorys(CancellationToken token)
    {
        await DetectOrphanDataAsync(null, null, [], ProfileTypeFilter.All, null, null, token);
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
                LastModified = new DateTimeOffset(record.LastModified.ToUniversalTime(), TimeSpan.Zero),
                LastAccessed = new DateTimeOffset(record.LastAccessed.ToUniversalTime(), TimeSpan.Zero)
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
    }

    /// <summary>
    /// 从服务器拉取指定时间范围内的所有历史记录(分页获取)
    /// </summary>
    private static async Task<List<HistoryRecordDto>> FetchRemoteRangeAsync(
        IOfficialSyncServer remoteServer,
        DateTime? before,
        DateTime? after,
        ProfileTypeFilter types,
        string? searchText,
        bool? starred,
        int pageLimit,
        DateTime? modifiedAfter,
        bool sortByLastAccessed,
        CancellationToken token)
    {
        var allRecords = new List<HistoryRecordDto>();
        var page = 1;

        while (page <= pageLimit && !token.IsCancellationRequested)
        {
            var pageRecords = await remoteServer.GetHistoryAsync(
                page: page,
                before: before,
                after: after,
                modifiedAfter: modifiedAfter,
                types: types,
                searchText: searchText,
                starred: starred,
                sortByLastAccessed: sortByLastAccessed);

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
        // 获取本地该范围内标记为 Synced 或 ServerOnly 的记录
        var localRecords = await _historyManager.GetHistoryAsync(
            types,
            starred,
            before,
            int.MaxValue,
            searchText,
            false,
            token).ConfigureAwait(false);

        await Task.Run(async () =>
        {
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
                    await _historyManager.RemoveHistory(localRecord, token).ConfigureAwait(false);
                    continue;
                }
                // 孤儿数据：服务器已删除，修改为 LocalOnly
                // await _logger.WriteAsync("HistorySyncer", $"检测到孤儿数据 [{localId}]，标记为 LocalOnly");
                localRecord.SyncStatus = HistorySyncStatus.LocalOnly;
                await _historyManager.PersistServerSyncedAsync(localRecord, token).ConfigureAwait(false);
            }
        }, token).ConfigureAwait(false);
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
        var localRecords = await _historyManager.GetHistoryAsync(
            types,
            starred,
            before,
            int.MaxValue,
            searchText,
            false,
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

    /// <summary>
    /// 同步所有未同步记录的数据：下载所有 IsLocalFileReady 为 false 的记录，上传所有 LocalOnly 的记录
    /// </summary>
    private async Task SyncPendingHistoryDataAsync(CancellationToken token)
    {
        var allRecords = await _historyManager.GetHistory().ConfigureAwait(false);
        allRecords = await Task.Run(allRecords.Where(r => r.IsDeleted == false).ToList, token);
        await SyncPendingDownloadsAsync(allRecords, token);
        await SyncPendingUploadsAsync(allRecords, token);
    }

    private async Task SyncPendingDownloadsAsync(List<HistoryRecord> allRecords, CancellationToken token)
    {
        var needDownload = await Task.Run(() => allRecords.Where(r => r.IsLocalFileReady is false).ToList(), token);
        foreach (var record in needDownload)
        {
            if (token.IsCancellationRequested)
                break;

            var profile = record.ToProfile();
            await _historyTransferQueue.EnqueueDownload(profile, forceResume: false, token);
        }
    }

    private async Task SyncPendingUploadsAsync(List<HistoryRecord> allRecords, CancellationToken token)
    {
        var needUpload = await Task.Run(
            () => allRecords
            .Where(r => r.SyncStatus == HistorySyncStatus.LocalOnly)
            .ToList(), token).ConfigureAwait(false);

        foreach (var record in needUpload)
        {
            if (token.IsCancellationRequested)
                break;

            var profile = record.ToProfile();
            var validationError = await ContentControlHelper.IsContentValid(profile, token);
            if (validationError != null)
            {
                continue;
            }

            await _historyTransferQueue.EnqueueUpload(profile, forceResume: false, token);
        }
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

    private async void OnHistoryAdded(HistoryRecord record)
    {
        try
        {
            // 如果已启用剪贴板同步且启用拉取，直接返回
            var syncConfig = _configManager.GetConfig<SyncConfig>();
            if (syncConfig.SyncSwitchOn && syncConfig.PullSwitchOn)
            {
                return;
            }

            var runtimeHistoryConfig = _runtimeConfig.GetConfig<RuntimeHistoryConfig>();
            if (runtimeHistoryConfig.EnableSyncHistory is false)
            {
                return;
            }

            var profile = record.ToProfile();
            // 使用 ContentControlHelper 过滤被 content control 过滤的记录
            var validationError = await ContentControlHelper.IsContentValid(profile, CancellationToken.None);
            if (validationError != null)
            {
                // 记录被过滤，跳过上传
                // _logger.Write("HistorySyncer", $"记录被过滤，跳过上传[{record.Hash}]: {validationError}");
                return;
            }

            await _historyTransferQueue.EnqueueUpload(profile, forceResume: true, CancellationToken.None);
            // _logger.Write("HistorySyncer", $"新增记录已加入传输队列[{record.Hash}]");
        }
        catch (Exception ex)
        {
            _logger.Write("HistorySyncer", $"新增记录处理失败[{record.Hash}]: {ex.Message}");
        }
    }

    private async void OnHistoryRemoved(HistoryRecord record)
    {
        try
        {
            if (record.SyncStatus == HistorySyncStatus.NeedSync)
            {
                await SyncOneAsync(record, CancellationToken.None);
            }

            var profileId = Profile.GetProfileId(record.Type, record.Hash);
            _historyTransferQueue.DeleteTask(profileId);
        }
        catch (Exception ex)
        {
            _logger.Write("HistorySyncer", $"删除事件同步失败[{record.Hash}]: {ex.Message}");
        }
    }
}

