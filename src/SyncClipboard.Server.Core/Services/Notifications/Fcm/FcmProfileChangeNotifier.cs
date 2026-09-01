using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Services.PushDevices;

namespace SyncClipboard.Server.Core.Services.Notifications.Fcm;

public sealed class FcmProfileChangeNotifier(
    IPushDeviceRegistry registry,
    IFcmPushClient fcmClient,
    ILogger<FcmProfileChangeNotifier> logger) : IProfileChangeNotifier
{
    public async Task NotifyProfileChanged(
        ProfileDto profile,
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
            await Task.WhenAll(registrations.Select(registration =>
                SendToDeviceAsync(registration, profile.Hash, cancellationToken)));
            logger.LogDebug(
                "FCM profile change hint processed for {DeviceCount} registered devices",
                registrations.Count);
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
