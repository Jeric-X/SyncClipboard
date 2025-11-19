using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Clipboard;

public class ClientProfileEnvProvider(ConfigManager config) : IProfileEnv
{
    public Task<string> GetWorkingDir(CancellationToken token)
    {
        var historyConfig = config.GetConfig<HistoryConfig>();
        if (historyConfig.EnableHistory)
        {
            return Task.FromResult(Env.HistoryFileFolder);
        }
        else
        {
            return Task.FromResult(Env.TemplateFileFolder);
        }
    }
}