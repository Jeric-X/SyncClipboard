using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities.Updater.Strategies;

namespace SyncClipboard.Core.Utilities.Updater;

internal static class UpdateServiceRegistration
{
    public static IServiceCollection AddUpdateInstallation(this IServiceCollection services)
    {
        services.AddSingleton<UpdateTaskCleaner>();
        // Concrete package strategies are added separately; downloads keep the manual installation flow.
        services.AddSingleton<IUpdateInstaller, UnsupportedUpdateInstaller>();
        return services;
    }
}
