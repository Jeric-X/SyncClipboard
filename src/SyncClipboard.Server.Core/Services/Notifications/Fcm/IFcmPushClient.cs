namespace SyncClipboard.Server.Core.Services.Notifications.Fcm;

public interface IFcmPushClient
{
    bool IsAvailable { get; }

    Task SendProfileChangedAsync(
        string pushToken,
        string profileHash,
        CancellationToken cancellationToken = default);
}
