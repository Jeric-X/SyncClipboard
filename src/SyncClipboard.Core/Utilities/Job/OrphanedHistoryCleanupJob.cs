using Quartz;
using SyncClipboard.Core.Utilities.History;

namespace SyncClipboard.Core.Utilities.Job;

public class OrphanedHistoryCleanupJob(HistoryManager historyManager) : IJob
{
    private readonly HistoryManager _historyManager = historyManager;

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => _historyManager.CleanupOrphanedHistoryFolders(cancellationToken), cancellationToken);
    }
}
