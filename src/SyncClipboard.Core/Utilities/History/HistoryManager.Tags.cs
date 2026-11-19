using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Utilities.History;

public partial class HistoryManager
{
    public async Task<Dictionary<string, int>> GetTagCountsAsync(CancellationToken token = default)
    {
        await _dbSemaphore.WaitAsync(token).ConfigureAwait(false);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());
        using var dbContext = _dbContextFactory();
        var tags = await dbContext.HistoryRecords.AsNoTracking().Where(r => !r.IsDeleted && r.Tags != null && r.Tags.Length > 0)
            .Select(r => r.Tags!).ToListAsync(token).ConfigureAwait(false);
        return tags.SelectMany(t => t.Distinct(StringComparer.Ordinal))
            .GroupBy(t => t, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
    }

    public async Task<int> EditTagsAsync(
        IReadOnlyCollection<HistoryRecordKey> keys, IEnumerable<string> added, IEnumerable<string> removed,
        CancellationToken token = default)
    {
        var additions = HistoryTags.NormalizeNames(added);
        var removals = HistoryTags.NormalizeNames(removed);
        if (additions.Length == 0 && removals.Length == 0)
            return 0;
        await _dbSemaphore.WaitAsync(token).ConfigureAwait(false);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());
        using var dbContext = _dbContextFactory();
        // Validate every target before applying any changes, including the resulting tag count.
        var updates = await GetTagUpdatesAsync(dbContext, keys, additions, removals, token).ConfigureAwait(false);
        return await SaveTagUpdatesAsync(dbContext, updates, token).ConfigureAwait(false);
    }

    public async Task DeleteTagAsync(string name, CancellationToken token = default)
    {
        name = HistoryTags.NormalizeName(name);
        await _dbSemaphore.WaitAsync(token).ConfigureAwait(false);
        using var guard = new ScopeGuard(() => _dbSemaphore.Release());
        using var dbContext = _dbContextFactory();
        var records = await dbContext.HistoryRecords.Where(r => !r.IsDeleted && r.Tags != null && r.Tags.Contains(name))
            .ToListAsync(token).ConfigureAwait(false);
        var updates = records.Select(record => (record, HistoryTags.Apply(record.Tags ?? [], [], [name]))).ToList();
        await SaveTagUpdatesAsync(dbContext, updates, token).ConfigureAwait(false);
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
        using var dbContext = _dbContextFactory();
        if (!allowMerge && await dbContext.HistoryRecords.AnyAsync(
            r => !r.IsDeleted && r.Tags != null && r.Tags.Contains(newName), token).ConfigureAwait(false))
            return false;
        var records = await dbContext.HistoryRecords.Where(r => !r.IsDeleted && r.Tags != null && r.Tags.Contains(name))
            .ToListAsync(token).ConfigureAwait(false);
        var updates = records.Select(record => (record, HistoryTags.Apply(record.Tags ?? [], [newName], [name]))).ToList();
        await SaveTagUpdatesAsync(dbContext, updates, token).ConfigureAwait(false);
        return true;
    }

    private async Task<int> SaveTagUpdatesAsync(HistoryDbContext dbContext, List<(HistoryRecord Record, string[] Tags)> updates,
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
        await dbContext.SaveChangesAsync(token).ConfigureAwait(false);
        foreach (var (record, _) in updates)
            HistoryUpdated?.Invoke(record);
        return updates.Count;
    }

    private static async Task<List<(HistoryRecord Record, string[] Tags)>> GetTagUpdatesAsync(
        HistoryDbContext dbContext, IReadOnlyCollection<HistoryRecordKey> keys,
        string[] additions, string[] removals, CancellationToken token)
    {
        var updates = new List<(HistoryRecord Record, string[] Tags)>();
        var visited = new HashSet<int>();
        foreach (var key in keys.Distinct())
        {
            var record = await Query(dbContext, key.Type, key.Hash, token).ConfigureAwait(false);
            if (record is null || record.IsDeleted || !visited.Add(record.ID))
                continue;
            // An explicit edit replaces the local tag set, even when its previous value was unknown.
            var current = record.Tags ?? [];
            var tags = HistoryTags.Apply(current, additions, removals);
            if (record.Tags is null || !current.SequenceEqual(tags))
                updates.Add((record, tags));
        }
        return updates;
    }
}
