using NativeNotification.Interface;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Commons;

public class StaticConfig : ConfigBase
{
    public StaticConfig(INotificationManager notification) : base(notification)
    {
        Path = Env.StaticConfigPath;
        Load();
    }

    protected override bool Save()
    {
        if (GetConfig<EnvConfig>() == new EnvConfig() && !File.Exists(Path))
        {
            return true;
        }

        return base.Save();
    }
}
