using Quartz;
using SyncClipboard.Core.Utilities.History;

namespace SyncClipboard.Core.Utilities.Job;

public class HistoryCleanupJob(HistoryManager historyManager) : IJob
{
    private readonly HistoryManager _historyManager = historyManager;

    public async Task Execute(IJobExecutionContext context)
    {
        await _historyManager.CleanupExpiredHistory(context.CancellationToken);
    }
}
