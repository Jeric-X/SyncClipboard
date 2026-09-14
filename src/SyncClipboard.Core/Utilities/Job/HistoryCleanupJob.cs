using Quartz;
using SyncClipboard.Core.Utilities.History;

namespace SyncClipboard.Core.Utilities.Job;

public class HistoryCleanupJob(HistoryManager historyManager) : IJob
{
    private readonly HistoryManager _historyManager = historyManager;

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        await _historyManager.CleanupExpiredHistory(cancellationToken);
    }
}
