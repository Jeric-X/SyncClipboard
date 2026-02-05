using Quartz;
using SyncClipboard.Core.Utilities.History;

namespace SyncClipboard.Core.Utilities.Job;

public class DeletedHistoryDataCleanupJob(HistoryManager historyManager) : IJob
{
    private readonly HistoryManager _historyManager = historyManager;

    public async Task Execute(IJobExecutionContext context)
    {
        await _historyManager.ClearDeletedHistoryData(context.CancellationToken);
    }
}
