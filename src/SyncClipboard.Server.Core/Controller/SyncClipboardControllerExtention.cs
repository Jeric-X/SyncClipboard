using SyncClipboard.Abstract;

namespace SyncClipboard.Server.Core.Controller;

public static class SyncClipboardControllerExtention
{
    public static void UseSyncCliboardServer(this WebApplication webApplication, ServerPara? serverConfig = null)
    {
        if (serverConfig is null || serverConfig.Passive == false)
        {
            new SyncClipboardController().Route(webApplication);
        }
        else
        {
            new SyncClipboardPassiveController(serverConfig.Services).Route(webApplication);
        }
    }
}
