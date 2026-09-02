namespace SyncClipboard.Server.Core.Services.Notifications;

public interface IProfileChangeNotifier
{
    Task NotifyProfileChanged(
        ProfileChangeNotification notification,
        CancellationToken cancellationToken = default);
}
