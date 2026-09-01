namespace SyncClipboard.Server.Core.Services.Notifications;

public interface IProfileChangeNotifier
{
    Task NotifyProfileChanged(ProfileDto profile, CancellationToken cancellationToken = default);
}
