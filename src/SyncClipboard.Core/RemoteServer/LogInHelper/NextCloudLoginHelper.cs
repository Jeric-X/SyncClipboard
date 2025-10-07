using SyncClipboard.Core.RemoteServer.Adapter.WebDavServer;

namespace SyncClipboard.Core.RemoteServer.LogInHelper;

public class NextCloudLoginHelper : ILoginHelper<WebDavConfig>
{
    public string TypeName => "NextCloud";
    public string LoginPageName => "NextCloudLogInPage";
}
