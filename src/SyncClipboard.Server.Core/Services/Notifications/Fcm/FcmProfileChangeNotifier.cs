namespace SyncClipboard.Server.Core.Services.Notifications.Fcm;

public sealed class FcmProfileChangeNotifier(
    IFcmProfileChangeQueue queue,
    IFcmPushClient fcmClient,
    ILogger<FcmProfileChangeNotifier> logger) : IProfileChangeNotifier
{
    public Task NotifyProfileChanged(
        ProfileChangeNotification notification,
        CancellationToken cancellationToken = default)
    {
        if (!fcmClient.IsAvailable)
        {
            logger.LogDebug("Skipping FCM profile change hint because provider is unavailable");
            return Task.CompletedTask;
        }

        var hint = new FcmProfileChangeHint(
            notification.Profile.Hash,
            notification.OriginDeviceId);
        if (queue.TryEnqueue(hint))
        {
            logger.LogDebug(
                "Queued FCM profile change hint for profile {ProfileHash}",
                notification.Profile.Hash);
        }
        else
        {
            logger.LogWarning(
                "Dropped FCM profile change hint for profile {ProfileHash} because the delivery queue is unavailable",
                notification.Profile.Hash);
        }

        return Task.CompletedTask;
    }
}
