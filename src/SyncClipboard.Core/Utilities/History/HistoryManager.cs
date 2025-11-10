using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Server.Core.Models;
using System.Diagnostics.CodeAnalysis;

namespace SyncClipboard.Core.Utilities.History;

public class HistoryManager
{
    public event Action<HistoryRecord>? HistoryAdded;
    public event Action<HistoryRecord>? HistoryRemoved;
    public event Action<HistoryRecord>? HistoryUpdated;

    private HistoryConfig _historyConfig = new();
    private readonly ILogger _logger;
    private HistoryDbContext _dbContext;
    private readonly SemaphoreSlim _dbSemaphore = new(1, 1);

    // 移除对 HistorySyncer 的直接依赖，改用事件让外部订阅以避免循环依赖

    public HistoryManager(ConfigManager configManager, ILogger logger)
    {
        _logger = logger;
        InitDatabaseContext();
        _historyConfig = configManager.GetListenConfig<HistoryConfig>(LoadConfig);
        LoadConfig(_historyConfig);
    }

    private async void LoadConfig(HistoryConfig config)
    {
        _historyConfig = config;

        await _dbSemaphore.WaitAsync();
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());

        if (!config.EnableSyncHistory && await SetRecordsMaxCount(_dbContext, config.MaxItemCount) != 0)
        {
            await _dbContext.SaveChangesAsync();
        }
    }

    private async Task<uint> SetRecordsMaxCount(HistoryDbContext _dbContext, uint maxCount, CancellationToken token = default)
    {
        // 如果 maxCount 为 0，使用默认值 1
        if (maxCount == 0)
        {
            maxCount = 1;
        }

        uint remoteCount = 0;
        var remoteRecords = _dbContext.HistoryRecords.Where(r => r.IsDeleted || !r.IsLocalFileReady).ToList();
        remoteCount += (uint)remoteRecords.Count;

        foreach (var record in remoteRecords)
        {
            await DeleteHistoryInternal(_dbContext, record, token);
            remoteCount++;
        }

        var records = _dbContext.HistoryRecords;
        uint count = (uint)records.Count();

        if (count > maxCount)
        {
            var toDeletes = records
                .Where(r => !r.Stared && !r.Pinned)
                .OrderBy(r => r.Timestamp)
                .Take((int)(count - maxCount))
                .ToArray();

            foreach (var record in toDeletes)
            {
                await DeleteHistoryInternal(_dbContext, record, token);
            }

            return count - maxCount + remoteCount;
        }
        return 0;
    }

    public static string? GetTempFolderPath(HistoryRecord record)
    {
        if (string.IsNullOrEmpty(record.Hash) || (record.Type != ProfileType.File && record.Type != ProfileType.Image && record.Type != ProfileType.Group))
            return null;

        var tempFolder = Path.Combine(Env.HistoryFileFolder, record.Hash);
        return Directory.Exists(tempFolder) ? tempFolder : null;
    }

    private async Task DeleteHistoryInternal(HistoryDbContext _dbContext, HistoryRecord record, CancellationToken token = default)
    {
        var entity = _dbContext.HistoryRecords.FirstOrDefault(r => r.Type == record.Type && r.Hash == record.Hash);
        if (entity != null)
        {
            var tempFolderPath = GetTempFolderPath(entity);
            if (!string.IsNullOrEmpty(tempFolderPath) && Directory.Exists(tempFolderPath))
            {
                try
                {
                    Directory.Delete(tempFolderPath, true);
                }
                catch (Exception ex)
                {
                    _logger.Write("HistoryManager", $"Failed to delete temp folder {tempFolderPath}: {ex.Message}");
                }
            }

            _dbContext.HistoryRecords.Remove(entity);
            await _dbContext.SaveChangesAsync(token);
            HistoryRemoved?.Invoke(record);
        }
    }

    public async Task AddHistory(HistoryRecord record, CancellationToken token)
    {
        await _dbSemaphore.WaitAsync(token);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());

        if (_dbContext.HistoryRecords.FirstOrDefault(r => r.Type == record.Type && r.Hash == record.Hash) is HistoryRecord entity)
        {
            entity.FilePath = record.FilePath;
            await _dbContext.SaveChangesAsync(token);
            HistoryUpdated?.Invoke(entity);
            return;
        }

        if (!_historyConfig.EnableSyncHistory)
        {
            await SetRecordsMaxCount(_dbContext, _historyConfig.MaxItemCount > 0 ? _historyConfig.MaxItemCount - 1 : 0, token);

            if (_historyConfig.MaxItemCount <= _dbContext.HistoryRecords.Count())
            {
                return;
            }
        }

        await _dbContext.HistoryRecords.AddAsync(record, token);
        await _dbContext.SaveChangesAsync(token);
        HistoryAdded?.Invoke(record);
    }

    public async Task<List<HistoryRecord>> GetHistory()
    {
        await _dbSemaphore.WaitAsync();
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());

        return _dbContext.HistoryRecords
            .OrderByDescending(r => r.Timestamp)
            .ToList();
    }

    public async Task<HistoryRecord?> GetHistoryRecord(string hash, ProfileType type, CancellationToken token)
    {
        await _dbSemaphore.WaitAsync(token);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());

        return _dbContext.HistoryRecords.FirstOrDefault(r => r.Type == type && r.Hash == hash);
    }

    public async Task UpdateHistory(HistoryRecord record, CancellationToken token = default)
    {
        await _dbSemaphore.WaitAsync(token);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());

        var entity = _dbContext.HistoryRecords.FirstOrDefault(r => r.Type == record.Type && r.Hash == record.Hash);
        if (entity != null)
        {
            entity.Stared = record.Stared;
            entity.Pinned = record.Pinned;
            entity.Text = record.Text;
            entity.FilePath = record.FilePath;
            entity.IsLocalFileReady = record.IsLocalFileReady;
            entity.LastModified = DateTime.UtcNow;
            entity.Version += 1;
            if (entity.SyncStatus != HistorySyncStatus.LocalOnly)
            {
                entity.SyncStatus = HistorySyncStatus.NeedSync;
            }
            await _dbContext.SaveChangesAsync(token);
            HistoryUpdated?.Invoke(entity);
            if (!_historyConfig.EnableSyncHistory)
            {
                await SetRecordsMaxCount(_dbContext, _historyConfig.MaxItemCount, token);
            }

            // 同步触发改由 HistorySyncer 订阅 HistoryUpdated 事件来完成，避免循环依赖
        }
    }

    /// <summary>
    /// 持久化一次从服务器回写或同步成功后的本地记录，不修改版本号和时间戳（这些已由同步逻辑确定）。
    /// 保留传入的 SyncStatus（通常为 Synced）。
    /// </summary>
    public async Task PersistServerSyncedAsync(HistoryRecord record, CancellationToken token = default)
    {
        await _dbSemaphore.WaitAsync(token);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());

        var entity = _dbContext.HistoryRecords.FirstOrDefault(r => r.Type == record.Type && r.Hash == record.Hash);
        if (entity is null)
        {
            // 如果本地不存在，直接添加（用于服务器回写新增场景）
            await _dbContext.HistoryRecords.AddAsync(record, token);
            await _dbContext.SaveChangesAsync(token);
            HistoryAdded?.Invoke(record);
            return;
        }

        // 同步服务器返回的并发字段与状态
        entity.Stared = record.Stared;
        entity.Pinned = record.Pinned;
        entity.IsDeleted = record.IsDeleted;
        entity.Version = record.Version;
        entity.LastModified = record.LastModified;
        entity.SyncStatus = record.SyncStatus; // 期望为 Synced

        await _dbContext.SaveChangesAsync(token);
        HistoryUpdated?.Invoke(entity);
    }

    public async Task DeleteHistory(HistoryRecord record, CancellationToken token = default)
    {
        await _dbSemaphore.WaitAsync(token);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());

        var entity = _dbContext.HistoryRecords.FirstOrDefault(r => r.Type == record.Type && r.Hash == record.Hash);
        if (entity is null)
        {
            return;
        }

        // Soft delete: 标记 IsDeleted 而不是物理删除；交由容量/过期清理逻辑后续清理。
        entity.IsDeleted = true;
        entity.LastModified = DateTime.UtcNow;
        entity.Version += 1;

        if (_historyConfig.EnableSyncHistory)
        {
            // 开启同步：需要向服务器同步删除，标记 NeedSync（LocalOnly 保持不变）
            if (entity.SyncStatus != HistorySyncStatus.LocalOnly)
            {
                entity.SyncStatus = HistorySyncStatus.NeedSync;
            }
        }
        else
        {
            // 未开启同步：保持 SyncStatus，不再标记 NeedSync
            // 可选：如果原来是 Synced 改为 LocalOnly；此处暂不更改保留原状态
        }

        await _dbContext.SaveChangesAsync(token);
        // 触发 Removed 事件供 UI 移除，并供同步器监听做删除同步（记录仍保留在数据库中）
        HistoryRemoved?.Invoke(entity);
    }

    public async Task<List<HistoryRecordDto>> SyncRemoteHistoryAsync(IEnumerable<HistoryRecordDto> remoteRecords, CancellationToken token = default)
    {
        var needUpdateToServer = new List<HistoryRecordDto>();
        if (remoteRecords.Any() == false)
            return needUpdateToServer;

        await _dbSemaphore.WaitAsync(token);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());

        bool changed = false;

        foreach (var dto in remoteRecords)
        {
            var entity = _dbContext.HistoryRecords.FirstOrDefault(r => r.Type == dto.Type && r.Hash == dto.Hash);
            if (entity == null)
            {
                var newRecord = dto.ToHistoryRecord();
                await _dbContext.HistoryRecords.AddAsync(newRecord, token);
                changed = true;
                HistoryAdded?.Invoke(newRecord);
            }
            else
            {
                if (entity.ShouldUpdateFromRemote(dto))
                {
                    entity.ApplyFromRemote(dto);
                    changed = true;
                    if (entity.IsDeleted)
                    {
                        HistoryRemoved?.Invoke(entity);
                    }
                    else
                    {
                        HistoryUpdated?.Invoke(entity);
                    }
                }
                else if (entity.IsLocalNewerThanRemote(dto))
                {
                    entity.SyncStatus = HistorySyncStatus.NeedSync;
                    changed = true;
                    HistoryUpdated?.Invoke(entity);
                    needUpdateToServer.Add(entity.ToHistoryRecordDto());
                }
            }
        }

        if (changed)
        {
            await _dbContext.SaveChangesAsync(token);
        }
        return needUpdateToServer;
    }

    public async Task CleanupExpiredHistory(CancellationToken token = default)
    {
        try
        {
            // When syncing history with server, local time-based cleanup is disabled
            if (!_historyConfig.EnableHistory || _historyConfig.EnableSyncHistory || _historyConfig.HistoryRetentionMinutes == 0)
            {
                return;
            }

            var cutoffTime = DateTime.UtcNow.AddMinutes(-_historyConfig.HistoryRetentionMinutes);

            await _dbSemaphore.WaitAsync(token);
            using var guard = new ScopeGuard(() => _dbSemaphore.Release());

            var expiredRecords = _dbContext.HistoryRecords
                .Where(r => r.Timestamp < cutoffTime && !r.Stared && !r.Pinned)
                .ToList();

            foreach (var record in expiredRecords)
            {
                await DeleteHistoryInternal(_dbContext, record, token);
            }

            if (expiredRecords.Count > 0)
            {
                _logger.Write("HistoryManager", $"Cleaned up {expiredRecords.Count} expired history records");
            }
        }
        catch (Exception ex)
        {
            _logger.Write("HistoryManager", $"Error during history cleanup: {ex.Message}");
        }
    }

    public void CleanupOrphanedHistoryFolders()
    {
        try
        {
            var historyFolder = Env.HistoryFileFolder;
            if (!Directory.Exists(historyFolder))
            {
                return;
            }

            using var _dbContext = new HistoryDbContext();
            var existingHashes = _dbContext.HistoryRecords
                .Where(r => r.Type == ProfileType.File || r.Type == ProfileType.Image || r.Type == ProfileType.Group)
                .Select(r => r.Hash)
                .ToHashSet();

            var directories = Directory.GetDirectories(historyFolder);
            var cutoffTime = DateTime.Now.AddDays(-7); // 7天前的时间点

            foreach (var dir in directories)
            {
                var dirName = Path.GetFileName(dir);

                try
                {
                    if (existingHashes.Contains(dirName))
                    {
                        continue;
                    }

                    var dirInfo = new DirectoryInfo(dir);
                    if (dirInfo.CreationTime <= cutoffTime)
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                            _logger.Write("HistoryManager", $"Deleted orphaned history folder: {dirName} (created: {dirInfo.CreationTime})");
                        }
                        catch (Exception ex)
                        {
                            _logger.Write("HistoryManager", $"Failed to delete orphaned folder {dir}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Write("HistoryManager", $"Error checking folder {dir}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Write("HistoryManager", $"Error during orphaned folder cleanup: {ex.Message}");
        }
    }

    [MemberNotNull(nameof(_dbContext))]
    private void InitDatabaseContext()
    {
        _dbContext = new HistoryDbContext();
        try
        {
            _dbContext.Database.ExecuteSqlRaw("DELETE FROM __EFMigrationsLock;");
        }
        catch { }

        var pendingMigrations = _dbContext.Database.GetPendingMigrations();
        if (pendingMigrations.Any())
        {
            _dbContext.Database.Migrate();
        }
        _dbContext.Database.EnsureCreated();
    }

    public async Task<List<HistoryRecord>> GetHistoryAsync(
        ProfileTypeFilter typeFilter,
        bool? started = null,
        DateTime? before = null,
        string? cursorProfileId = null,
        int size = int.MaxValue,
        string? searchText = null,
        CancellationToken token = default)
    {
        await _dbSemaphore.WaitAsync(token);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());

        var query = _dbContext.HistoryRecords.Where(r => r.IsDeleted == false);

        if (typeFilter != ProfileTypeFilter.All)
        {
            var includedTypes = Enum.GetValues(typeof(ProfileType))
                .Cast<ProfileType>()
                .Where(t => (typeFilter & (ProfileTypeFilter)(1 << (int)t)) != 0)
                .ToList();

            if (includedTypes.Count == 0)
            {
                return [];
            }

            query = query.Where(r => includedTypes.Contains(r.Type));
        }

        if (before.HasValue)
        {
            var beforeUtc = before.Value;
            query = query.Where(r => r.Timestamp <= beforeUtc);
        }
        if (started.HasValue)
        {
            query = query.Where(r => r.Stared == started.Value);
        }

        if (!string.IsNullOrEmpty(searchText))
        {
            query = query.Where(r => EF.Functions.Like(r.Text, $"%{searchText}%"));
        }

        query = query.OrderByDescending(r => r.Timestamp).ThenByDescending(r => r.ID);

        if (!string.IsNullOrEmpty(cursorProfileId) && Profile.ParseProfileId(cursorProfileId, out var cursorType, out var cursorHash))
        {
            int position = query
                .AsEnumerable()
                .Select((r, idx) => new { r.Hash, r.Type, Index = idx })
                .Where(r => r.Hash == cursorHash && r.Type == cursorType)
                .Select(x => x.Index)
                .FirstOrDefault(-1);
            query = query.Skip(position + 1);
        }
        return await query.Take(size).ToListAsync(token);
    }
}