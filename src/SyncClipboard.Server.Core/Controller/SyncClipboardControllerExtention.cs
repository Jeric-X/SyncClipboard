namespace SyncClipboard.Server.Core.Controller;

public static class SyncClipboardControllerExtention
{
    public static IServiceCollection AddSyncClipboardServer(this IServiceCollection services)
    {
        services.AddSingleton<SyncClipboardController>();
        services.AddSignalR();
        return services;
    }

    public static void UseSyncClipboardServer(this WebApplication app)
    {
        SyncClipboardController.MapRoutes(app);
    }
}
