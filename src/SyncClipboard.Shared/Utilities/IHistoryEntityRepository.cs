using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace SyncClipboard.Shared.Utilities;

public interface IHistoryEntityRepository<TEntity, TDeleteOrderKey> where TEntity : class
{
    // public DbContext DbContext { get; }
    public DbSet<TEntity> RecordDbSet { get; }

    public Expression<Func<TEntity, bool>> QueryCount { get; }
    public Expression<Func<TEntity, bool>> QueryToDeleteByOverCount { get; }
    public Expression<Func<TEntity, TDeleteOrderKey>> QueryDeleteOrderBy { get; }

    public Task DeleteHistoryByOverCount(TEntity entity, CancellationToken token);
}