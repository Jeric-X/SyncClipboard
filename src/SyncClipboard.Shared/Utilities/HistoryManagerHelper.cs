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

        uint count = (uint)await records.Where(repository.QueryCount).CountAsync(token);

        if (count > maxCount)
        {
            var toDeletes = records.Where(repository.QueryToDeleteByOverCount);
            toDeletes = toDeletes.OrderBy(repository.QueryDeleteOrderBy);
            toDeletes = toDeletes.Take((int)(count - maxCount));
            var toDeletesList = toDeletes.ToList();

            foreach (var record in toDeletesList)
            {
                await repository.DeleteHistoryByOverCount(record, token);
            }

            return count - maxCount;
        }
        return 0;
    }
}
