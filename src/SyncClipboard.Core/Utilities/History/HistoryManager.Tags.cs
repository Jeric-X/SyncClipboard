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
        return await SaveTagUpdatesAsync(updates, token).ConfigureAwait(false);
    }

    public async Task DeleteTagAsync(string name, CancellationToken token = default)
    {
        name = HistoryTags.NormalizeName(name);
        await _dbSemaphore.WaitAsync(token).ConfigureAwait(false);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());
        var records = await _dbContext.HistoryRecords.Where(r => !r.IsDeleted && r.Tags.Contains(name))
            .ToListAsync(token).ConfigureAwait(false);
        var updates = records.Select(record => (record, HistoryTags.Apply(record.Tags, [], [name]))).ToList();
        await SaveTagUpdatesAsync(updates, token).ConfigureAwait(false);
    }

    // A false result requires explicit merge confirmation; no records have been changed.
    public async Task<bool> RenameTagAsync(string name, string newName, bool allowMerge = false,
        CancellationToken token = default)
    {
        name = HistoryTags.NormalizeName(name);
        newName = HistoryTags.NormalizeName(newName);
        if (name == newName)
            return true;
        await _dbSemaphore.WaitAsync(token).ConfigureAwait(false);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());
        if (!allowMerge && await _dbContext.HistoryRecords.AnyAsync(
            r => !r.IsDeleted && r.Tags.Contains(newName), token).ConfigureAwait(false))
            return false;
        var records = await _dbContext.HistoryRecords.Where(r => !r.IsDeleted && r.Tags.Contains(name))
            .ToListAsync(token).ConfigureAwait(false);
        var updates = records.Select(record => (record, HistoryTags.Apply(record.Tags, [newName], [name]))).ToList();
        await SaveTagUpdatesAsync(updates, token).ConfigureAwait(false);
        return true;
    }

    private async Task<int> SaveTagUpdatesAsync(List<(HistoryRecord Record, string[] Tags)> updates,
        CancellationToken token)
    {
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
