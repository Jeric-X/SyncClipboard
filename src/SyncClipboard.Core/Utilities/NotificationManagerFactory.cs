using NativeNotification.Interface;
using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Core.Utilities;

internal static class NotificationManagerFactory
{
    public static INotificationManager Create(Func<INotificationManager> createManager, ILogger logger)
    {
        try
        {
            return createManager();
        }
        catch (Exception exception)
        {
            logger.Write(nameof(NotificationManagerFactory),
                $"Native notification initialization failed; notifications are disabled for this session: {exception}");
            return new NoOpNotificationManager();
        }
    }
}
