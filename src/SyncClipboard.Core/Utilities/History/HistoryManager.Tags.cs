using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Utilities.History;

public partial class HistoryManager
{
    public async Task<Dictionary<string, int>> GetTagCountsAsync(CancellationToken token = default)
    {
        await _dbSemaphore.WaitAsync(token).ConfigureAwait(false);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());
        var tags = await _dbContext.HistoryRecords.AsNoTracking().Where(r => !r.IsDeleted && r.Tags.Length > 0)
            .Select(r => r.Tags).ToListAsync(token).ConfigureAwait(false);
        return tags.SelectMany(t => t.Distinct(StringComparer.Ordinal))
            .GroupBy(t => t, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
    }

    public async Task<int> EditTagsAsync(
        IReadOnlyCollection<HistoryRecordKey> keys, IEnumerable<string> added, IEnumerable<string> removed,
        CancellationToken token = default)
    {
        var additions = HistoryTags.NormalizeNames(added);
        var removals = HistoryTags.NormalizeNames(removed);
        await _dbSemaphore.WaitAsync(token).ConfigureAwait(false);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());
        // Validate every target before applying any changes, including the resulting tag count.
        var updates = await GetTagUpdatesAsync(keys, additions, removals, token).ConfigureAwait(false);
        if (updates.Count == 0)
            return 0;

        var modified = DateTime.UtcNow;
        foreach (var (record, tags) in updates)
        {
            record.Tags = tags;
            record.Version++;
            record.LastModified = modified;
            if (record.SyncStatus != HistorySyncStatus.LocalOnly)
                record.SyncStatus = HistorySyncStatus.NeedSync;
        }
        await _dbContext.SaveChangesAsync(token).ConfigureAwait(false);
        foreach (var (record, _) in updates)
            HistoryUpdated?.Invoke(record);
        return updates.Count;
    }

    private async Task<List<(HistoryRecord Record, string[] Tags)>> GetTagUpdatesAsync(
        IReadOnlyCollection<HistoryRecordKey> keys, string[] additions, string[] removals, CancellationToken token)
    {
        var updates = new List<(HistoryRecord Record, string[] Tags)>();
        var visited = new HashSet<int>();
        foreach (var key in keys.Distinct())
        {
            var record = await Query(key.Type, key.Hash, token).ConfigureAwait(false);
            if (record is null || record.IsDeleted || !visited.Add(record.ID))
                continue;
            var tags = HistoryTags.Apply(record.Tags, additions, removals);
            if (!record.Tags.SequenceEqual(tags))
                updates.Add((record, tags));
        }
        return updates;
    }
}
