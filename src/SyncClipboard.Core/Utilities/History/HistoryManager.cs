using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Server.Core.Models;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace SyncClipboard.Core.Utilities.History;

public class HistoryManager : IHistoryEntityRepository<HistoryRecord, DateTime>
{
    public event Action<HistoryRecord>? HistoryAdded;
    public event Action<HistoryRecord>? HistoryRemoved;
    public event Action<HistoryRecord>? HistoryUpdated;

    private HistoryConfig _historyConfig = new();
    private RuntimeHistoryConfig _runtimeHistoryConfig = new();
    private readonly ILogger _logger;
    private HistoryDbContext _dbContext;
    private readonly IProfileEnv _profileEnv;
    private readonly SemaphoreSlim _dbSemaphore = new(1, 1);
    private readonly HistoryManagerHelper<HistoryRecord, DateTime> _historyManagerHelper;
    public bool EnableCleanup { get; set; } = true;

    public DbSet<HistoryRecord> RecordDbSet => _dbContext.HistoryRecords;

    public HistoryManager(ConfigManager configManager, ILogger logger, IProfileEnv profileEnv,
        [FromKeyedServices(Env.RuntimeConfigName)] ConfigBase runtimeConfig)
    {
        _logger = logger;
        _profileEnv = profileEnv;
        InitDatabaseContext();
        _historyManagerHelper = new(this);
        _runtimeHistoryConfig = runtimeConfig.GetListenConfig<RuntimeHistoryConfig>(LoadRuntimeHistoryConfig);
        _historyConfig = configManager.GetListenConfig<HistoryConfig>(LoadHistoryConfig);
        LoadConfig(_runtimeHistoryConfig, _historyConfig);
    }

    private void LoadHistoryConfig(HistoryConfig config)
    {
        _historyConfig = config;
        LoadConfig(_runtimeHistoryConfig, _historyConfig);
    }

    private void LoadRuntimeHistoryConfig(RuntimeHistoryConfig config)
    {
        _runtimeHistoryConfig = config;
        LoadConfig(_runtimeHistoryConfig, _historyConfig);
    }

    private async void LoadConfig(RuntimeHistoryConfig runtimeConfig, HistoryConfig historyConfig)
    {
        await _dbSemaphore.WaitAsync();
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());

        if (EnableCleanup && await _historyManagerHelper.SetRecordsMaxCount(historyConfig.MaxItemCount) != 0)
        {
            await _dbContext.SaveChangesAsync();
        }

        if (runtimeConfig.EnableSyncHistory)
        {
            return;
        }

        await DeleteObsoleteRemoteRecords();
    }

    private async Task<uint> DeleteObsoleteRemoteRecords(CancellationToken token = default)
    {
        var remoteRecords = _dbContext.HistoryRecords.Where(r => r.IsDeleted || !r.IsLocalFileReady).ToList();
        uint remoteCount = (uint)remoteRecords.Count;
        if (remoteCount == 0)
            return 0;

        foreach (var record in remoteRecords)
        {
            record.SyncStatus = HistorySyncStatus.LocalOnly;
            await RemoveHistory(record, token);
        }

        await _dbContext.SaveChangesAsync(token);
        return remoteCount;
    }

    public string? GetRecordWorkingDir(HistoryRecord record)
    {
        if (string.IsNullOrEmpty(record.Hash))
            return null;

        var persistentDir = _profileEnv.GetPersistentDir();
        var workingDir = Profile.QueryGetWorkingDir(persistentDir, record.Type, record.Hash);

        return workingDir;
    }

    private async Task DeleteWorkingDirAsync(HistoryRecord record, CancellationToken token)
    {
        var workingDir = GetRecordWorkingDir(record);
        await Task.Run(() =>
        {
            if (!string.IsNullOrEmpty(workingDir) && Directory.Exists(workingDir))
            {
                try
                {
                    Directory.Delete(workingDir, true);
                }
                catch (Exception ex)
                {
                    _logger.Write("HistoryManager", $"Failed to delete temp folder {workingDir}: {ex.Message}");
                }
            }
        }, token);
    }

    public async Task RemoveHistory(HistoryRecord record, CancellationToken token)
    {
        var entity = await Query(record.Type, record.Hash, token);
        if (entity != null)
        {
            await DeleteWorkingDirAsync(entity, token);

            _dbContext.HistoryRecords.Remove(entity);
            await _dbContext.SaveChangesAsync(token);
            HistoryRemoved?.Invoke(record);
        }
    }

    public async Task AddLocalProfile(Profile profile, bool updateLastAccessed = true, CancellationToken token = default)
    {
        var record = await ToHistoryRecord(profile, token);
        record.Hash = record.Hash.ToUpperInvariant();
        await _dbSemaphore.WaitAsync(token);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());

        if (_dbContext.HistoryRecords.FirstOrDefault(r => r.Type == record.Type && EF.Functions.Like(r.Hash, record.Hash)) is HistoryRecord entity)
        {
            if (string.IsNullOrEmpty(entity.Text) && !string.IsNullOrEmpty(record.Text))
            {
                entity.Text = record.Text;
            }
            entity.FilePath = record.FilePath;
            entity.IsLocalFileReady = true;
            entity.IsDeleted = false;
            entity.LastModified = DateTime.UtcNow;
            if (updateLastAccessed)
            {
                entity.LastAccessed = DateTime.UtcNow;
            }
            await _dbContext.SaveChangesAsync(token);
            HistoryUpdated?.Invoke(entity);
            return;
        }

        if (EnableCleanup && _historyConfig.MaxItemCount > 0)
        {
            await _historyManagerHelper.SetRecordsMaxCount(_historyConfig.MaxItemCount - 1, token);
        }
        await _dbContext.HistoryRecords.AddAsync(record, token);
        await _dbContext.SaveChangesAsync(token);
        HistoryAdded?.Invoke(record);
    }

    public async Task AddRemoteProfile(Profile profile, CancellationToken token)
    {
        var record = await ToRemoteHistoryRecord(profile, token);
        record.Hash = record.Hash.ToUpperInvariant();
        if (string.IsNullOrEmpty(record.Hash))
            return;

        await _dbSemaphore.WaitAsync(token);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());

        if (_dbContext.HistoryRecords.FirstOrDefault(r => r.Type == record.Type && EF.Functions.Like(r.Hash, record.Hash)) is HistoryRecord entity)
        {
            entity.IsDeleted = false;
            entity.SyncStatus = HistorySyncStatus.Synced;
            await _dbContext.SaveChangesAsync(token);
            HistoryUpdated?.Invoke(entity);
            return;
        }

        record.IsLocalFileReady = false;
        record.SyncStatus = HistorySyncStatus.Synced;
        await _dbContext.HistoryRecords.AddAsync(record, token);
        await _dbContext.SaveChangesAsync(token);
        HistoryAdded?.Invoke(record);
    }

    public async Task<List<HistoryRecord>> GetHistory(CancellationToken? token = null)
    {
        token ??= CancellationToken.None;
        await _dbSemaphore.WaitAsync(token.Value);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());

        return _dbContext.HistoryRecords
            .OrderByDescending(r => r.Timestamp)
            .ToList();
    }

    public async Task<HistoryRecord?> GetHistoryRecord(string hash, ProfileType type, CancellationToken token)
    {
        await _dbSemaphore.WaitAsync(token);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());

        return await Query(type, hash, token);
    }

    public async Task<HistoryRecord> GetOrCreateHistoryRecord(Profile profile, CancellationToken token)
    {
        await _dbSemaphore.WaitAsync(token);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());

        var entity = await Query(profile.Type, await profile.GetHash(token), token);
        entity ??= await ToHistoryRecord(profile, token);
        return entity;
    }

    public async Task UpdateHistoryProperty(HistoryRecord record, CancellationToken token = default)
    {
        await _dbSemaphore.WaitAsync(token);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());

        var entity = await Query(record.Type, record.Hash, token);
        if (entity != null)
        {
            entity.Stared = record.Stared;
            entity.Pinned = record.Pinned;
            entity.Text = record.Text;
            entity.FilePath = record.FilePath;
            entity.IsLocalFileReady = record.IsLocalFileReady;
            entity.LastModified = DateTime.UtcNow;
            entity.Version++;
            if (entity.SyncStatus != HistorySyncStatus.LocalOnly)
            {
                entity.SyncStatus = HistorySyncStatus.NeedSync;
            }
            await _dbContext.SaveChangesAsync(token);
            HistoryUpdated?.Invoke(entity);
        }
    }

    public async Task UpdateHistoryLocalInfo(HistoryRecord record, CancellationToken token = default)
    {
        await _dbSemaphore.WaitAsync(token);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());
        var entity = await Query(record.Type, record.Hash, token);
        if (entity != null)
        {
            entity.IsLocalFileReady = record.IsLocalFileReady;
            entity.FilePath = record.FilePath;
            await _dbContext.SaveChangesAsync(token);
            HistoryUpdated?.Invoke(entity);
        }
    }

    /// <summary>
    /// 持久化一次从服务器回写或同步成功后的本地记录，不修改版本号和时间戳（这些已由同步逻辑确定）。
    /// 保留传入的 SyncStatus（通常为 Synced）。
    /// </summary>
    public async Task<HistoryRecord> PersistServerSyncedAsync(HistoryRecord record, CancellationToken token = default)
    {
        await _dbSemaphore.WaitAsync(token);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());

        var entity = await Query(record.Type, record.Hash, token);
        if (entity is null)
        {
            // 如果本地不存在，直接添加（用于服务器回写新增场景）
            record.Hash = record.Hash.ToUpperInvariant();
            await _dbContext.HistoryRecords.AddAsync(record, token);
            await _dbContext.SaveChangesAsync(token);
            HistoryAdded?.Invoke(record);
            return record;
        }

        // 同步服务器返回的并发字段与状态
        entity.Stared = record.Stared;
        entity.Pinned = record.Pinned;
        entity.IsDeleted = record.IsDeleted;
        entity.Version = record.Version;
        entity.LastModified = record.LastModified;
        entity.LastAccessed = record.LastAccessed;
        entity.SyncStatus = record.SyncStatus; // 期望为 Synced

        await _dbContext.SaveChangesAsync(token);
        TriggleUpdateOrDeleteEvent(entity);
        return entity;
    }

    public async Task DeleteHistory(HistoryRecord record, CancellationToken token = default)
    {
        await _dbSemaphore.WaitAsync(token);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());

        var entity = await Query(record.Type, record.Hash, token);
        if (entity is null)
        {
            return;
        }

        entity.IsDeleted = true;
        entity.LastModified = DateTime.UtcNow;
        entity.Version += 1;
        if (entity.FilePath.Length > 0)
        {
            entity.FilePath = [];
            entity.IsLocalFileReady = false;
        }
        await DeleteWorkingDirAsync(entity, token);

        if (_runtimeHistoryConfig.EnableSyncHistory)
        {
            if (entity.SyncStatus != HistorySyncStatus.LocalOnly)
            {
                entity.SyncStatus = HistorySyncStatus.NeedSync;
            }
        }

        await _dbContext.SaveChangesAsync(token);
        HistoryRemoved?.Invoke(entity);
    }

    public async Task<List<HistoryRecord>> SyncRemoteHistoryAsync(IEnumerable<HistoryRecordDto> remoteRecords, CancellationToken token = default)
    {
        List<HistoryRecord> addedRecords = [];
        if (remoteRecords.Any() == false)
            return addedRecords;

        await Task.Run(async () =>
        {
            foreach (var dto in remoteRecords)
            {
                await _dbSemaphore.WaitAsync(token).ConfigureAwait(false);
                using var guard = new ScopeGuard(() => _dbSemaphore.Release());

                var entity = await Query(dto.Type, dto.Hash, token).ConfigureAwait(false);
                if (entity == null)
                {
                    if (dto.IsDeleted == false)
                    {
                        var newRecord = dto.ToHistoryRecord();
                        await _dbContext.HistoryRecords.AddAsync(newRecord, token).ConfigureAwait(false);
                        HistoryAdded?.Invoke(newRecord);
                        addedRecords.Add(newRecord);
                    }
                }
                else
                {
                    if (entity.ShouldUpdateFromRemote(dto))
                    {
                        entity.ApplyChangesFromRemote(dto);
                        entity.SyncStatus = HistorySyncStatus.Synced;
                    }
                    else if (entity.IsLocalNewerThanRemote(dto))
                    {
                        entity.SyncStatus = HistorySyncStatus.NeedSync;
                    }
                    else
                    {
                        entity.SyncStatus = HistorySyncStatus.Synced;
                    }
                    entity.ApplyBasicFromRemote(dto);
                    TriggleUpdateOrDeleteEvent(entity);
                }
                await _dbContext.SaveChangesAsync(token).ConfigureAwait(false);
            }
        }, token);

        return addedRecords;
    }

    private void TriggleUpdateOrDeleteEvent(HistoryRecord record)
    {
        if (record.IsDeleted)
        {
            HistoryRemoved?.Invoke(record);
        }
        else
        {
            HistoryUpdated?.Invoke(record);
        }
    }

    private async Task RemoveSoftDeletedOutOfDateRecords(CancellationToken token = default)
    {
        await _dbSemaphore.WaitAsync(token);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());

        var cutoffTime = DateTime.UtcNow.AddDays(-30);
        var toDeletes = _dbContext.HistoryRecords
            .Where(r => r.IsDeleted && r.LastModified < cutoffTime);

        if (!toDeletes.Any())
        {
            return;
        }

        _dbContext.HistoryRecords.RemoveRange(toDeletes);
        await _dbContext.SaveChangesAsync(token);
    }

    public async Task ClearDeletedHistoryData(CancellationToken token = default)
    {
        await _dbSemaphore.WaitAsync(token);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());

        var deletedRecords = _dbContext.HistoryRecords
            .Where(r => r.IsDeleted && r.FilePath.Length > 0 && r.IsLocalFileReady)
            .ToList();

        if (deletedRecords.Count == 0)
        {
            return;
        }

        foreach (var record in deletedRecords)
        {
            record.FilePath = [];
            record.IsLocalFileReady = false;
            await DeleteWorkingDirAsync(record, token);
        }

        _dbContext.HistoryRecords.RemoveRange(deletedRecords);
        await _dbContext.SaveChangesAsync(token);

        _logger.Write("HistoryManager", $"Cleared {deletedRecords.Count} deleted history records and their data");
    }

    public async Task CleanupExpiredHistory(CancellationToken token = default)
    {
        try
        {
            if (!EnableCleanup)
            {
                return;
            }

            if (_runtimeHistoryConfig.EnableSyncHistory)
            {
                await RemoveSoftDeletedOutOfDateRecords(token);
                return;
            }

            if (_historyConfig.HistoryRetentionMinutes == 0)
            {
                return;
            }

            var cutoffTime = DateTime.UtcNow.AddMinutes(-_historyConfig.HistoryRetentionMinutes);

            await _dbSemaphore.WaitAsync(token);
            using var guard = new ScopeGuard(() => _dbSemaphore.Release());

            var expiredRecords = _dbContext.HistoryRecords
                .Where(r => r.Timestamp < cutoffTime && !r.Stared && !r.Pinned && r.SyncStatus == HistorySyncStatus.LocalOnly)
                .ToList();

            foreach (var record in expiredRecords)
            {
                await RemoveHistory(record, token);
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
        int minSize = int.MaxValue,
        string? searchText = null,
        bool sortByLastAccessed = false,
        CancellationToken token = default)
    {
        await _dbSemaphore.WaitAsync(token).ConfigureAwait(false);
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
            query = sortByLastAccessed
                ? query.Where(r => r.LastAccessed < beforeUtc)
                : query.Where(r => r.Timestamp < beforeUtc);
        }
        if (started.HasValue)
        {
            query = query.Where(r => r.Stared == started.Value);
        }

        if (!string.IsNullOrEmpty(searchText))
        {
            query = query.Where(r => EF.Functions.Like(r.Text, $"%{searchText}%"));
        }

        query = sortByLastAccessed
            ? query.OrderByDescending(r => r.LastAccessed).ThenByDescending(r => r.ID)
            : query.OrderByDescending(r => r.Timestamp).ThenByDescending(r => r.ID);

        // 先取minSize条记录
        var initialRecords = await query.Take(minSize).ToListAsync(token).ConfigureAwait(false);

        // 如果记录数量少于minSize，说明已经取完所有记录
        if (initialRecords.Count < minSize)
        {
            return initialRecords;
        }

        // 获取最后一条记录的时间戳和ID
        var lastRecord = initialRecords[^1];
        var lastTimestamp = sortByLastAccessed ? lastRecord.LastAccessed : lastRecord.Timestamp;
        var lastId = lastRecord.ID;

        // 查询是否还有其他记录具有相同的时间戳但ID更小（即在排序后位于更后面）
        var additionalRecords = sortByLastAccessed
            ? await query.Where(r => r.LastAccessed == lastTimestamp && r.ID < lastId).ToListAsync(token).ConfigureAwait(false)
            : await query.Where(r => r.Timestamp == lastTimestamp && r.ID < lastId).ToListAsync(token).ConfigureAwait(false);

        // 合并结果
        if (additionalRecords.Count > 0)
        {
            initialRecords.AddRange(additionalRecords);
        }

        initialRecords.ForEach(r =>
        {
            r.Timestamp = DateTime.SpecifyKind(r.Timestamp, DateTimeKind.Utc);
        });

        return initialRecords;
    }

    public async Task ClearAllLocalAsync(CancellationToken token = default)
    {
        await _dbSemaphore.WaitAsync(token);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());

        var all = await _dbContext.HistoryRecords.ToListAsync(token);
        foreach (var record in all)
        {
            try
            {
                record.SyncStatus = HistorySyncStatus.LocalOnly;
                await RemoveHistory(record, token);
            }
            catch (Exception ex)
            {
                _logger.Write("HistoryManager", $"Failed to delete history record {record.Type}-{record.Hash}: {ex.Message}");
            }
        }

        await Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(Env.HistoryFileFolder))
                {
                    foreach (var dir in Directory.GetDirectories(Env.HistoryFileFolder))
                    {
                        try { Directory.Delete(dir, true); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Write("HistoryManager", $"Failed to cleanup history folders: {ex.Message}");
            }
        }, token);
    }

    public async Task<HistoryRecord> ToHistoryRecord(Profile profile, CancellationToken token)
    {
        var profileEntity = await profile.Persist(_profileEnv.GetPersistentDir(), token);
        var record = new HistoryRecord
        {
            Text = profileEntity.Text,
            Type = profileEntity.Type,
            Size = profileEntity.Size,
            Hash = profileEntity.Hash,
            FilePath = profileEntity.FilePaths
        };
        return record;
    }

    public static async Task<HistoryRecord> ToRemoteHistoryRecord(Profile profile, CancellationToken token)
    {
        var record = new HistoryRecord
        {
            Text = profile.DisplayText,
            Type = profile.Type,
            Size = await profile.GetSize(token),
            Hash = await profile.GetHash(token),
        };
        return record;
    }

    private Task<HistoryRecord?> Query(ProfileType type, string hash, CancellationToken token)
    {
        return _dbContext.HistoryRecords.FirstOrDefaultAsync(r => EF.Functions.Like(r.Hash, hash) && r.Type == type, token);
    }

    public Task DeleteHistoryByOverCount(HistoryRecord entity, CancellationToken token)
    {
        return RemoveHistory(entity, token);
    }

    public Expression<Func<HistoryRecord, bool>> QueryToDeleteByOverCount => entity => !entity.Stared &&
        !entity.Pinned && entity.SyncStatus == HistorySyncStatus.LocalOnly;

    public Expression<Func<HistoryRecord, DateTime>> QueryDeleteOrderBy => entity => entity.Timestamp;

    public Expression<Func<HistoryRecord, bool>> QueryCount => entity => entity.SyncStatus == HistorySyncStatus.LocalOnly;
}