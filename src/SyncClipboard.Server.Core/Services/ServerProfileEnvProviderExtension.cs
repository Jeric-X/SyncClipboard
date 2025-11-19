namespace SyncClipboard.Server.Core.Services;

public static class ServerProfileEnvProviderExtension
{
    public static IApplicationBuilder UseServerProfileEnvProvider(this WebApplication app)
    {
        Profile.SetGlobalProfileEnvProvider(app.Services.GetRequiredService<ServerProfileEnvProvider>());
        return app;
    }

    public static IServiceCollection AddServerProfileEnvProvider(this IServiceCollection services)
    {
        services.AddSingleton<ServerProfileEnvProvider>();
        return services;
    }
}