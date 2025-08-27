using SyncClipboard.Abstract;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Utilities.History;

public class HistoryManager : IDisposable
{
    private readonly string DbName = "history.db";

    public event Action<HistoryRecord>? HistoryAdded;
    public event Action<HistoryRecord>? HistoryRemoved;

    //private readonly HistoryDbContext _dbContext = new HistoryDbContext();

    public async Task AddHistory(HistoryRecord record, CancellationToken token = default)
    {
        using var _dbContext = new HistoryDbContext(DbName);
        _dbContext.Database.EnsureCreated();
        if (
            _dbContext.HistoryRecords.Any(
                r => r.Type == record.Type
                &&
                (
                    (r.Type == ProfileType.Text && r.Text == record.Text)
                    ||
                    (r.Type != ProfileType.Text && r.Hash == record.Hash)
                )
            )
        )
        {
            return;
        }

        await _dbContext.HistoryRecords.AddAsync(record, token);
        await _dbContext.SaveChangesAsync(token);
        HistoryAdded?.Invoke(record);
    }

    public List<HistoryRecord> GetHistory()
    {
        using var _dbContext = new HistoryDbContext(DbName);
        _dbContext.Database.EnsureCreated();
        return _dbContext.HistoryRecords
            .OrderByDescending(r => r.Timestamp)
            .ToList();
    }

    public async Task DeleteHistory(HistoryRecord record, CancellationToken token = default)
    {
        using var _dbContext = new HistoryDbContext(DbName);
        _dbContext.Database.EnsureCreated();
        var entity = _dbContext.HistoryRecords.FirstOrDefault(r => r.Type == record.Type && r.Text == record.Text && r.Hash == record.Hash);
        if (entity != null)
        {
            _dbContext.HistoryRecords.Remove(entity);
            await _dbContext.SaveChangesAsync(token);
            HistoryRemoved?.Invoke(record);
        }
    }

    ~HistoryManager()
    {
        Dispose();
    }

    public void Dispose()
    {
        //_dbContext.Dispose();
        GC.SuppressFinalize(this);
    }
}