using SyncClipboard.Abstract.Notification;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Commons;

public class StaticConfig : ConfigBase
{
    public StaticConfig(INotification notification)
    {
        Notification = notification;
        Path = Env.StaticConfigPath;
        Load();
    }

    protected override void Save()
    {
        if (GetConfig<EnvConfig>() == new EnvConfig() && !File.Exists(Path))
        {
            return;
        }
        else
        {
            base.Save();
        }
    }
}