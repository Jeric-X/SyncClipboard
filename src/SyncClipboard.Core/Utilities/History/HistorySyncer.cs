using SyncClipboard.Core.Models;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Core.Utilities.History;

/// <summary>
/// 负责将本地需要同步的历史记录推送到服务器，并处理冲突回写。
/// </summary>
public class HistorySyncer
{
    private readonly HistoryManager _historyManager;
    private readonly RemoteClipboardServerFactory _remoteServerFactory;
    private readonly ILogger _logger;
    private const string BasePath = "api/history"; // Used only when adapter exposes raw HTTP; otherwise we rely on IHistorySyncServer

    public HistorySyncer(HistoryManager historyManager, RemoteClipboardServerFactory remoteServerFactory, ILogger logger)
    {
        _historyManager = historyManager;
        _remoteServerFactory = remoteServerFactory;
        _logger = logger;

        // 订阅 HistoryManager 的事件：更新与删除（软删除）均可能需要同步
        _historyManager.HistoryUpdated += OnHistoryUpdated;
        _historyManager.HistoryRemoved += OnHistoryRemoved;
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
            return; // 没有需要同步的
        }

        foreach (var record in needSync)
        {
            await SyncOneAsync(record, token);
        }

        // 每条记录在 SyncOneAsync 内已即时持久化，这里无需再次保存。
    }

    /// <summary>
    /// 同步单条历史记录到服务器，处理并发冲突与本地回写。
    /// </summary>
    /// <param name="record">本地历史记录</param>
    /// <param name="token">取消令牌</param>
    /// <returns>返回是否已将本地状态更新为 Synced</returns>
    public async Task SyncOneAsync(HistoryRecord record, CancellationToken token = default)
    {
        if (_remoteServerFactory.Current is not IHistorySyncServer remoteServer)
        {
            _logger.Write("HistorySyncer", "当前远程服务器不支持历史记录同步");
            return;
        }

        try
        {
            var dto = new HistoryRecordUpdateDto
            {
                Stared = record.Stared,
                Pinned = record.Pinned,
                IsDelete = record.IsDeleted ? true : null,
                Version = record.Version,
                LastModified = new DateTimeOffset(record.LastModified.ToUniversalTime(), TimeSpan.Zero)
            };
            await remoteServer.UpdateHistoryAsync(record.Type, record.Hash, dto, token);
            // 成功：不再返回payload，直接标记为 Synced 并持久化
            record.SyncStatus = HistorySyncStatus.Synced;
            await _historyManager.PersistServerSyncedAsync(record, token);
        }
        catch (SyncClipboard.Core.Exceptions.RemoteHistoryConflictException ex)
        {
            // 409 冲突：使用服务器返回的记录覆盖本地
            if (ex.Server != null)
            {
                record.ApplyFromServerUpdateDto(ex.Server);
                await _historyManager.PersistServerSyncedAsync(record, token);
            }
            _logger.Write("HistorySyncer", $"并发冲突，已回写服务器版本[{record.Hash}]");
        }
        catch (SyncClipboard.Core.Exceptions.RemoteHistoryNotFoundException)
        {
            // 404: 标记为仅本地
            record.SyncStatus = HistorySyncStatus.LocalOnly;
            await _historyManager.PersistServerSyncedAsync(record, token);
            _logger.Write("HistorySyncer", $"服务器不存在记录，标记为 LocalOnly[{record.Hash}]");
        }
        catch (Exception ex)
        {
            _logger.Write("HistorySyncer", $"同步记录异常[{record.Hash}]: {ex.Message}");
            return;
        }
    }

    // 旧的 ApplyServerUpdate 已被统一的扩展方法替代

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
        // 软删除场景：如果启用了同步且该记录不是 LocalOnly，则需要推送删除
        try
        {
            if (record.IsDeleted && record.SyncStatus != HistorySyncStatus.LocalOnly)
            {
                // 删除同步也走 UpdateHistory（设置 IsDelete=true）逻辑
                await SyncOneAsync(record, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.Write("HistorySyncer", $"删除事件同步失败[{record.Hash}]: {ex.Message}");
        }
    }
}

