namespace SyncClipboard.Server.Core.Services.Notifications.Fcm;

public sealed class InvalidFcmRegistrationException(
    string message,
    Exception innerException) : Exception(message, innerException);
