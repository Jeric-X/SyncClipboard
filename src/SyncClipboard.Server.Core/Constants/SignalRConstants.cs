using SyncClipboard.Server.Core.Hubs;

namespace SyncClipboard.Server.Core.Constants;

public static class SignalRConstants
{
    public const string HubPath = "/SyncClipboardHub";
    public const string RemoteProfileChangedMethod = nameof(ISyncClipboardClient.RemoteProfileChanged);
}