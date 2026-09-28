using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Core.Utilities.Updater;

internal static class UpdateInstallationServices
{
    public static IServiceCollection AddUpdateInstallation(this IServiceCollection services)
    {
        services.AddSingleton<UpdateInstallCleanup>();
        services.AddSingleton<UpdateInstallHelper>();
        services.AddSingleton<IUpdateInstallStrategy, WindowsZipInstaller>();
        services.AddSingleton<IUpdateInstaller, UpdateInstaller>();
        return services;
    }
}
