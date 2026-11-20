using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Models;
using System.Linq.Expressions;

namespace SyncClipboard.Core.Utilities.History;

public partial class HistoryManager
{
    private HistoryManagerHelper<HistoryRecord, DateTime> CreateCleanupHelper(HistoryDbContext dbContext)
        => new(new CleanupRepository(this, dbContext));

    // The helper and its tracked records belong to the caller's database operation.
    private sealed class CleanupRepository(HistoryManager manager, HistoryDbContext dbContext)
        : IHistoryEntityRepository<HistoryRecord, DateTime>
    {
        public DbSet<HistoryRecord> RecordDbSet => dbContext.HistoryRecords;
        public Expression<Func<HistoryRecord, bool>> QueryCount => entity => entity.SyncStatus == HistorySyncStatus.LocalOnly;
        public Expression<Func<HistoryRecord, bool>> AutoDeletePredicate => manager.AutoDeletePredicate;
        public Expression<Func<HistoryRecord, DateTime>> QueryLastActivityTime => entity =>
            entity.LastModified > entity.LastAccessed ? entity.LastModified : entity.LastAccessed;
        public uint HistoryRetentionMinutes => manager._historyConfig.HistoryRetentionMinutes;
        public Expression<Func<HistoryRecord, DateTime>> QueryDeleteOrderBy => QueryLastActivityTime;

        public Task MarkForDeletionAsync(HistoryRecord entity, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            dbContext.HistoryRecords.Remove(entity);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken token) => dbContext.SaveChangesAsync(token);

        public Task OnRecordDeletedAsync(HistoryRecord entity, CancellationToken token)
            => manager.OnRecordDeletedAsync(entity, token);
    }
}
