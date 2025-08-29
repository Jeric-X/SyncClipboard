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
        if (SetRecordsMaxCount(_dbContext.HistoryRecords, config.MaxItemCount) != 0)
        {
            await _dbContext.SaveChangesAsync();
        }
    }

    private uint SetRecordsMaxCount(DbSet<HistoryRecord> records, uint maxCount)
    {
        uint count = (uint)records.Count();

        if (count > maxCount)
        {
            var toDeletes = records.OrderBy(r => r.Timestamp).Take((int)(count - maxCount)).ToArray();
            records.RemoveRange(toDeletes);
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
            HistoryUpdated?.Invoke(entity);
            return;
        }

        SetRecordsMaxCount(_dbContext.HistoryRecords, _historyConfig.MaxItemCount > 0 ? _historyConfig.MaxItemCount - 1 : 0);

        if (_historyConfig.MaxItemCount <= 0)
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