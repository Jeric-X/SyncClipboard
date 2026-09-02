using FirebaseAdmin.Messaging;

namespace SyncClipboard.Server.Core.Services.Notifications.Fcm;

public static class FcmMessageFactory
{
    public static Message CreateProfileChangedMessage(string pushToken, string profileHash)
    {
        return new Message
        {
#pragma warning disable CS0618 // S4/M7 protocol uses FCM registration tokens, not Firebase installation IDs.
            Token = pushToken,
#pragma warning restore CS0618
            Data = new Dictionary<string, string>
            {
                ["v"] = "1",
                ["type"] = "clipboard_changed",
                ["hash"] = profileHash
            },
            Android = new AndroidConfig
            {
                CollapseKey = "clipboard",
                Priority = Priority.Normal
            }
        };
    }
}
