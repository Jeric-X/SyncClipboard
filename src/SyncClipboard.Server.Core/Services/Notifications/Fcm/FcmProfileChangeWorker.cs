namespace SyncClipboard.Server.Core.Services.Notifications.Fcm;

public sealed class FcmProfileChangeWorker(
    IFcmProfileChangeQueue queue,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<FcmProfileChangeWorker> logger) : BackgroundService
{
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            FcmProfileChangeHint hint;
            try
            {
                hint = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            using var scope = serviceScopeFactory.CreateScope();
            var delivery = scope.ServiceProvider.GetRequiredService<FcmProfileChangeDelivery>();
            using var deliveryTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            deliveryTimeout.CancelAfter(DeliveryTimeout);
            try
            {
                await delivery.DeliverAsync(hint, deliveryTimeout.Token);
            }
            catch (OperationCanceledException) when (
                !stoppingToken.IsCancellationRequested && deliveryTimeout.IsCancellationRequested)
            {
                logger.LogWarning(
                    "FCM profile change delivery timed out for profile {ProfileHash}",
                    hint.ProfileHash);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    ex,
                    "Unexpected failure delivering FCM profile change hint for profile {ProfileHash}",
                    hint.ProfileHash);
            }
        }
    }
}
