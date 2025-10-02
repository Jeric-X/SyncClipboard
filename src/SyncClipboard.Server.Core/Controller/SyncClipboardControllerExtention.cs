using SyncClipboard.Abstract;

namespace SyncClipboard.Server.Core.Controller;

public static class SyncClipboardControllerExtention
{
    public static void UseSyncCliboardServer(this WebApplication webApplication)
    {
        new SyncClipboardController().Route(webApplication);
    }
}
