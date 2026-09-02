namespace SyncClipboard.Server.Core.Services.Notifications;

public sealed record ProfileChangeNotification(
    ProfileDto Profile,
    string? OriginDeviceId = null);
