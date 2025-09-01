using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Utilities.History;

public class HistoryManager
{
    public event Action<HistoryRecord>? HistoryAdded;
    public event Action<HistoryRecord>? HistoryRemoved;
    public event Action<HistoryRecord>? HistoryUpdated;

    private HistoryConfig _historyConfig = new();

    public HistoryManager(ConfigManager configManager)
    {
        MigrateDatabase();
        _historyConfig = configManager.GetListenConfig<HistoryConfig>(LoadConfig);
        LoadConfig(_historyConfig);
    }

    private async void LoadConfig(HistoryConfig config)
    {
        _historyConfig = config;

        using var _dbContext = await GetDbContext();
        if (await SetRecordsMaxCount(_dbContext, config.MaxItemCount) != 0)
        {
            await _dbContext.SaveChangesAsync();
        }
    }

    private async Task<uint> SetRecordsMaxCount(HistoryDbContext _dbContext, uint maxCount, CancellationToken token = default)
    {
        var records = _dbContext.HistoryRecords;
        uint count = (uint)records.Count();

        if (count > maxCount)
        {
            var toDeletes = records
                .Where(r => !r.Stared && !r.Pinned)
                .OrderBy(r => r.Timestamp)
                .Take((int)(count - maxCount))
                .ToArray();
            records.RemoveRange(toDeletes);
            await _dbContext.SaveChangesAsync(token);
            toDeletes.ForEach(r => HistoryRemoved?.Invoke(r));
            return count - maxCount;
        }
        return 0;
    }

    public async Task AddHistory(HistoryRecord record, CancellationToken token = default)
    {
        using var _dbContext = await GetDbContext();
        if (_dbContext.HistoryRecords.FirstOrDefault(r => r.Type == record.Type && r.Hash == record.Hash) is HistoryRecord entity)
        {
            entity.Timestamp = record.Timestamp;
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

    public static async Task<List<HistoryRecord>> GetHistory()
    {
        using var _dbContext = await GetDbContext();
        return _dbContext.HistoryRecords
            .OrderByDescending(r => r.Timestamp)
            .ToList();
    }

    public async Task UpdateHistory(HistoryRecord record, CancellationToken token = default)
    {
        using var _dbContext = await GetDbContext();
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
        using var _dbContext = await GetDbContext();
        var entity = _dbContext.HistoryRecords.FirstOrDefault(r => r.Type == record.Type && r.Hash == record.Hash);
        if (entity != null)
        {
            _dbContext.HistoryRecords.Remove(entity);
            await _dbContext.SaveChangesAsync(token);
            HistoryRemoved?.Invoke(record);
        }
    }

    private static async Task<HistoryDbContext> GetDbContext()
    {
        var _dbContext = new HistoryDbContext();
        await _dbContext.Database.EnsureCreatedAsync();
        return _dbContext;
    }

    private static void MigrateDatabase()
    {
        var dbContext = new HistoryDbContext();
        try
        {
            dbContext.Database.ExecuteSqlRaw("DELETE FROM __EFMigrationsLock;");
        }
        catch { }

        var pendingMigrations = dbContext.Database.GetPendingMigrations();
        if (pendingMigrations.Any())
        {
            dbContext.Database.Migrate();
        }
    }
}