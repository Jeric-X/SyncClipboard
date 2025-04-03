using SyncClipboard.Abstract.Notification;

namespace SyncClipboard.Core.Commons;

public class StaticConfig : ConfigBase
{
    protected override INotification Notification { get; }

    public StaticConfig(INotification notification)
    {
        Notification = notification;
        Path = Env.StaticConfigPath;
        Load();
    }
}