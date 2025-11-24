namespace SyncClipboard.Server.Core.Services;

public static class ServerProfileEnvProviderExtension
{
    public static IServiceCollection AddServerProfileEnvProvider(this IServiceCollection services)
    {
        services.AddSingleton<ServerEnvProvider>();
        services.AddSingleton<IProfileEnv>(sp => sp.GetRequiredService<ServerEnvProvider>());
        return services;
    }
}