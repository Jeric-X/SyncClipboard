using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Interfaces;
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

        if (await SetRecordsMaxCount(_dbContext, config.MaxItemCount) != 0)
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

            return count - maxCount;
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
            entity.Timestamp = record.Timestamp;
            entity.FilePath = record.FilePath;
            await _dbContext.SaveChangesAsync(token);
            HistoryRemoved?.Invoke(entity);
            HistoryAdded?.Invoke(entity);
            return;
        }

        await SetRecordsMaxCount(_dbContext, _historyConfig.MaxItemCount > 0 ? _historyConfig.MaxItemCount - 1 : 0, token);

        if (_historyConfig.MaxItemCount <= _dbContext.HistoryRecords.Count())
        {
            return;
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
            entity.Timestamp = record.Timestamp;
            entity.Text = record.Text;
            entity.FilePath = record.FilePath;
            await _dbContext.SaveChangesAsync(token);
            HistoryUpdated?.Invoke(entity);
            await SetRecordsMaxCount(_dbContext, _historyConfig.MaxItemCount, token);
        }
    }

    public async Task DeleteHistory(HistoryRecord record, CancellationToken token = default)
    {
        await _dbSemaphore.WaitAsync(token);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());

        await DeleteHistoryInternal(_dbContext, record, token);
    }

    public async Task CleanupExpiredHistory(CancellationToken token = default)
    {
        try
        {
            if (!_historyConfig.EnableHistory || _historyConfig.HistoryRetentionMinutes == 0)
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
}