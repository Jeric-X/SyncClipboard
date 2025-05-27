using SyncClipboard.Abstract.Notification;

namespace SyncClipboard.Core.Commons;

public class RuntimeConfig(INotification notification) : ConfigBase(Env.RuntimeConfigPath, notification)
{
}

