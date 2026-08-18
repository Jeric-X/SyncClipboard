using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace SyncClipboard.Shared.Utilities;

public class HistoryManagerHelper<TEntity, TDeleteOrderKey>(IHistoryEntityRepository<TEntity, TDeleteOrderKey> repository) where TEntity : class
{
    // private readonly DbContext dBContext = repository.DbContext;
    private readonly DbSet<TEntity> records = repository.RecordDbSet;

    public async Task<uint> SetRecordsMaxCount(uint maxCount, CancellationToken token = default)
    {
        // 0 means no limit
        if (maxCount == 0)
        {
            return 0;
        }

        const int BatchSize = 500;
        uint deleted = 0;

        while (!token.IsCancellationRequested)
        {
            await repository.OnBatchStartAsync(token);
            try
            {
                uint count = (uint)await records.Where(repository.QueryCount).CountAsync(token);
                if (count <= maxCount)
                {
                    break;
                }

                var take = (int)Math.Min(BatchSize, count - maxCount);
                var batch = await records.Where(repository.QueryToDeleteByOverCount)
                    .OrderBy(repository.QueryDeleteOrderBy)
                    .Take(take)
                    .ToListAsync(token);

                if (batch.Count == 0)
                {
                    break;
                }

                foreach (var record in batch)
                {
                    await repository.MarkForDeletionAsync(record, token);
                }
                await repository.SaveChangesAsync(token);
                foreach (var record in batch)
                {
                    await repository.OnRecordDeletedAsync(record, token);
                }
                deleted += (uint)batch.Count;
            }
            finally
            {
                await repository.OnBatchEndAsync(token);
            }
        }

        return deleted;
    }

    public async Task<uint> RemoveExpiredInBatchesAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken token = default)
    {
        const int BatchSize = 500;
        uint deleted = 0;

        while (!token.IsCancellationRequested)
        {
            await repository.OnBatchStartAsync(token);
            try
            {
                var batch = await records.Where(predicate)
                    .OrderBy(repository.QueryDeleteOrderBy)
                    .Take(BatchSize)
                    .ToListAsync(token);

                if (batch.Count == 0)
                {
                    break;
                }

                foreach (var record in batch)
                {
                    await repository.MarkForDeletionAsync(record, token);
                }
                await repository.SaveChangesAsync(token);
                foreach (var record in batch)
                {
                    await repository.OnRecordDeletedAsync(record, token);
                }
                deleted += (uint)batch.Count;
            }
            finally
            {
                await repository.OnBatchEndAsync(token);
            }
        }

        return deleted;
    }
}
