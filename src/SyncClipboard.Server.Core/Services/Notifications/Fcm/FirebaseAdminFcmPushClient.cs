using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;
using SyncClipboard.Server.Core.Models;

namespace SyncClipboard.Server.Core.Services.Notifications.Fcm;

public sealed class FirebaseAdminFcmPushClient : IFcmPushClient, IDisposable
{
    private readonly ILogger<FirebaseAdminFcmPushClient> _logger;
    private FirebaseApp? _firebaseApp;
    private FirebaseMessaging? _messaging;

    public FirebaseAdminFcmPushClient(
        IOptions<AppSettings> options,
        ILogger<FirebaseAdminFcmPushClient> logger)
    {
        _logger = logger;
        if (!options.Value.EnableFcmPush)
        {
            logger.LogDebug("FCM push provider is disabled by configuration");
            return;
        }

        try
        {
            var appOptions = new AppOptions
            {
                Credential = GoogleCredential.GetApplicationDefault(),
                ProjectId = string.IsNullOrWhiteSpace(options.Value.FirebaseProjectId)
                    ? null
                    : options.Value.FirebaseProjectId.Trim()
            };
            _firebaseApp = FirebaseApp.Create(
                appOptions, $"syncclipboard-{Guid.NewGuid():N}");
            _messaging = FirebaseMessaging.GetMessaging(_firebaseApp);
            logger.LogInformation("FCM push provider initialized");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FCM push provider initialization failed; push remains unavailable");
        }
    }

    public bool IsAvailable => _messaging is not null;

    public async Task SendProfileChangedAsync(
        string pushToken,
        string profileHash,
        CancellationToken cancellationToken = default)
    {
        if (_messaging is null)
        {
            throw new InvalidOperationException("FCM push provider is unavailable");
        }

        var message = FcmMessageFactory.CreateProfileChangedMessage(pushToken, profileHash);
        try
        {
            await _messaging.SendAsync(message, cancellationToken);
        }
        catch (FirebaseMessagingException ex)
            when (ex.MessagingErrorCode == MessagingErrorCode.Unregistered)
        {
            throw new InvalidFcmRegistrationException(
                "FCM registration is permanently unregistered",
                ex);
        }
    }

    public void Dispose()
    {
        _messaging = null;
        _firebaseApp?.Delete();
        _firebaseApp = null;
        GC.SuppressFinalize(this);
    }
}
