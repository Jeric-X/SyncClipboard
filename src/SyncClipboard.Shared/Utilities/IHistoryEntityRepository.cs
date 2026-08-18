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

    /// <summary>
    /// 仅将实体标记为待删除（修改字段或 DbContext 状态），不调用 SaveChanges，不触发副作用。
    /// 契约：helper 路径下 entity 来自 ToListAsync，已被 DbContext 跟踪；外部单独调用时实现需自行处理 Detached。
    /// </summary>
    public Task MarkForDeletionAsync(TEntity entity, CancellationToken token);

    /// <summary>
    /// 持久化所有未保存的变更到数据库。批处理中由 helper 在 MarkForDeletionAsync 循环后统一调用一次。
    /// </summary>
    public Task<int> SaveChangesAsync(CancellationToken token);

    /// <summary>
    /// SaveChanges 成功后对单条已删除记录触发的副作用：SignalR 广播 / UI 事件 / 删除物理文件。
    /// helper 仍按循环调用，不批量处理。
    /// </summary>
    public Task OnRecordDeletedAsync(TEntity entity, CancellationToken token);

    public Task OnBatchStartAsync(CancellationToken token) => Task.CompletedTask;
    public Task OnBatchEndAsync(CancellationToken token) => Task.CompletedTask;
}