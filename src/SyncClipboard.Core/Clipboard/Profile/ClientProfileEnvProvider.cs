using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Clipboard;

public class ClientProfileEnvProvider(ConfigManager config) : IProfileEnv
{
    public string GetPersistentDir()
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

    public string GetHistoryPersistentDir()
    {
        return Env.HistoryFileFolder;
    }
}