using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Services.PushDevices;

namespace SyncClipboard.Server.Core.Services.Notifications.Fcm;

public sealed class FcmProfileChangeDelivery(
    IPushDeviceRegistry registry,
    IFcmPushClient fcmClient,
    ILogger<FcmProfileChangeDelivery> logger)
{
    public async Task DeliverAsync(
        FcmProfileChangeHint hint,
        CancellationToken cancellationToken = default)
    {
        if (!fcmClient.IsAvailable)
        {
            logger.LogDebug("Skipping FCM profile change hint because provider is unavailable");
            return;
        }

        IReadOnlyList<PushDeviceRegistration> registrations;
        try
        {
            registrations = await registry.GetByProviderAsync("fcm", cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to read FCM push registrations");
            return;
        }

        var recipients = registrations
            .Where(registration => !string.Equals(
                registration.DeviceId,
                hint.OriginDeviceId,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        var excludedDeviceCount = registrations.Count - recipients.Count;
        if (excludedDeviceCount > 0)
        {
            logger.LogDebug(
                "Excluded originating device {OriginDeviceId} from FCM profile change hint",
                hint.OriginDeviceId);
        }

        foreach (var registration in recipients)
        {
            await SendToDeviceAsync(registration, hint.ProfileHash, cancellationToken);
        }

        logger.LogDebug(
            "FCM profile change hint processed for {DeviceCount} devices; {ExcludedDeviceCount} origin devices excluded",
            recipients.Count,
            excludedDeviceCount);
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
        catch (InvalidFcmRegistrationException ex)
        {
            try
            {
                var removed = await registry.RemoveIfTokenMatchesAsync(
                    registration.DeviceId,
                    registration.PushToken,
                    cancellationToken);
                if (removed)
                {
                    logger.LogWarning(
                        ex,
                        "Removed permanently invalid FCM registration for device {DeviceId}",
                        registration.DeviceId);
                }
                else
                {
                    logger.LogWarning(
                        ex,
                        "Did not remove FCM registration for device {DeviceId} because its token changed",
                        registration.DeviceId);
                }
            }
            catch (Exception removeException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    removeException,
                    "Failed to remove permanently invalid FCM registration for device {DeviceId}",
                    registration.DeviceId);
            }
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
