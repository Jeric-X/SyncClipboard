using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Clipboard;

public class ClientProfileEnvProvider(ConfigManager config) : IProfileEnv
{
    public string GetWorkingDir()
    {
        var historyConfig = config.GetConfig<HistoryConfig>();
        if (historyConfig.EnableHistory)
        {
            return Env.HistoryFileFolder;
        }
        else
        {
            return Env.TemplateFileFolder;
        }
    }
}