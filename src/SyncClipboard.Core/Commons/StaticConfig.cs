using SyncClipboard.Abstract.Notification;

namespace SyncClipboard.Core.Commons;

public class StaticConfig : ConfigBase
{
    public StaticConfig(INotification notification)
    {
        Notification = notification;
        Path = Env.StaticConfigPath;
        Load();
    }
}