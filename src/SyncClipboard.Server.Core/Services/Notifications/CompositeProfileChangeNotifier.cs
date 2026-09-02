namespace SyncClipboard.Server.Core.Services.Notifications;

public sealed class CompositeProfileChangeNotifier(
    IEnumerable<IProfileChangeNotifier> notifiers) : IProfileChangeNotifier
{
    public async Task NotifyProfileChanged(
        ProfileChangeNotification notification,
        CancellationToken cancellationToken = default)
    {
        foreach (var notifier in notifiers)
        {
            await notifier.NotifyProfileChanged(notification, cancellationToken);
        }
    }
}
