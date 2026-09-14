using Quartz;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Updater;

namespace SyncClipboard.Core.Utilities.Job;

public class UpdateJob(ConfigManager configManager, UpdateChecker updateChecker, IThreadDispatcher dispatcher) : IJob
{
    private readonly ConfigManager configManager = configManager;
    private readonly UpdateChecker updateChecker = updateChecker;
    private readonly IThreadDispatcher dispatcher = dispatcher;

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (configManager.GetConfig<ProgramConfig>().CheckUpdateOnStartUp)
        {
            return new ValueTask(dispatcher.RunOnMainThreadAsync(updateChecker.RunAutoUpdateFlow));
        }
        return ValueTask.CompletedTask;
    }
}
