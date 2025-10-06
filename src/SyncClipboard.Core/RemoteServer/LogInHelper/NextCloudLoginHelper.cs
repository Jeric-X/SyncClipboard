using SyncClipboard.Core.RemoteServer.Adapter.WebDavAdapter;

namespace SyncClipboard.Core.RemoteServer.LogInHelper;

public class NextCloudLoginHelper : ILoginHelper<WebDavConfig>
{
    public string TypeName => "NextCloud";
    public string LoginPageName => "NextCloudLogInPage";
}
