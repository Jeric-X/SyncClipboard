using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Utilities.History;

public class HistoryManager : IDisposable
{
    public event Action? HistoryChanged;

    private readonly HistoryDbContext _dbContext = new HistoryDbContext();

    public async Task AddHistory(HistoryRecord record, CancellationToken token = default)
    {
        _dbContext.Database.EnsureCreated();
        if (_dbContext.HistoryRecords.Any(r => r.Type == record.Type && r.Text == record.Text && r.Hash == record.Hash))
        {
            return;
        }

        await _dbContext.HistoryRecords.AddAsync(record, token);
        _dbContext.HistoryRecords.Distinct();
        await _dbContext.SaveChangesAsync(token);
        HistoryChanged?.Invoke();
    }

    public List<HistoryRecord> GetHistory()
    {
        _dbContext.Database.EnsureCreated();
        return _dbContext.HistoryRecords
            .OrderByDescending(r => r.Timestamp)
            .ToList();
    }

    public async Task DeleteHistory(HistoryRecord record, CancellationToken token = default)
    {
        _dbContext.Database.EnsureCreated();
        var entity = _dbContext.HistoryRecords.FirstOrDefault(r => r == record);
        if (entity != null)
        {
            _dbContext.HistoryRecords.Remove(entity);
            await _dbContext.SaveChangesAsync(token);
            HistoryChanged?.Invoke();
        }
    }

    ~HistoryManager()
    {
        Dispose();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }
}   