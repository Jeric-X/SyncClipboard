using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.History.HistoryExport;

namespace SyncClipboard.Core.Utilities.History;

public partial class HistoryManager
{
    public async Task<IReadOnlyList<HistoryExportRecord>> GetExportSnapshotAsync(
        IReadOnlyList<HistoryRecordKey>? selected, CancellationToken token)
    {
        await _dbSemaphore.WaitAsync(token).ConfigureAwait(false);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());
        if (selected is { Count: 0 })
            return [];
        var records = await _dbContext.HistoryRecords.AsNoTracking().Where(record => !record.IsDeleted)
            .ToListAsync(token).ConfigureAwait(false);
        var keys = selected?.Select(key => new HistoryRecordKey(key.Type, key.Hash.ToUpperInvariant())).ToHashSet();
        return records.Where(record => keys is null || keys.Contains(new(record.Type, record.Hash.ToUpperInvariant())))
            .Select(HistoryExportRecord.FromRecord).ToArray();
    }
}
