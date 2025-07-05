using Quartz;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Updater;

namespace SyncClipboard.Core.Utilities.Job;

public class UpdateJob(ConfigManager configManager, UpdateChecker updateChecker) : IJob
{
    private readonly ConfigManager configManager = configManager;
    private readonly UpdateChecker updateChecker = updateChecker;

    public Task Execute(IJobExecutionContext context)
    {
        if (configManager.GetConfig<ProgramConfig>().CheckUpdateOnStartUp)
        {
            return updateChecker.RunUpdateFlow();
        }
        return Task.CompletedTask;
    }
}