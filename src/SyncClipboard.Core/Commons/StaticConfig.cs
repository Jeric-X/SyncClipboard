using NativeNotification.Interface;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Commons;

public class StaticConfig : ConfigBase
{
    public StaticConfig(INotificationManager notification) : this(Env.StaticConfigPath, notification)
    {
    }

    internal StaticConfig(string path, INotificationManager? notification = null) : base(notification)
    {
        Path = path;
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
