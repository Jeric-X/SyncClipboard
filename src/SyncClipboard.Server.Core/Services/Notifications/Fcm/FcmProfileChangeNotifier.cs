using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Services.PushDevices;

namespace SyncClipboard.Server.Core.Services.Notifications.Fcm;

public sealed class FcmProfileChangeNotifier(
    IPushDeviceRegistry registry,
    IFcmPushClient fcmClient,
    ILogger<FcmProfileChangeNotifier> logger) : IProfileChangeNotifier
{
    public async Task NotifyProfileChanged(
        ProfileChangeNotification notification,
        CancellationToken cancellationToken = default)
    {
        if (!fcmClient.IsAvailable)
        {
            logger.LogDebug("Skipping FCM profile change hint because provider is unavailable");
            return;
        }

        try
        {
            var registrations = await registry.GetByProviderAsync("fcm", cancellationToken);
            var recipients = registrations
                .Where(registration => !string.Equals(
                    registration.DeviceId,
                    notification.OriginDeviceId,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            var excludedDeviceCount = registrations.Count - recipients.Count;
            if (excludedDeviceCount > 0)
            {
                logger.LogDebug(
                    "Excluded originating device {OriginDeviceId} from FCM profile change hint",
                    notification.OriginDeviceId);
            }

            await Task.WhenAll(recipients.Select(registration =>
                SendToDeviceAsync(
                    registration,
                    notification.Profile.Hash,
                    cancellationToken)));
            logger.LogDebug(
                "FCM profile change hint processed for {DeviceCount} devices; {ExcludedDeviceCount} origin devices excluded",
                recipients.Count,
                excludedDeviceCount);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to process FCM profile change hints");
        }
    }

    private async Task SendToDeviceAsync(
        PushDeviceRegistration registration,
        string profileHash,
        CancellationToken cancellationToken)
    {
        try
        {
            await fcmClient.SendProfileChangedAsync(
                registration.PushToken, profileHash, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                ex,
                "Failed to send FCM profile change hint to device {DeviceId}",
                registration.DeviceId);
        }
    }
}
