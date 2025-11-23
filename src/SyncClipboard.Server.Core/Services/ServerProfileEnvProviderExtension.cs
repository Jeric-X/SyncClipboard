namespace SyncClipboard.Server.Core.Services;

public static class ServerProfileEnvProviderExtension
{
    public static IServiceCollection AddServerProfileEnvProvider(this IServiceCollection services)
    {
        services.AddSingleton<IProfileEnv, ServerProfileEnvProvider>();
        return services;
    }
}