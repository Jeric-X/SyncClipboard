using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities.Updater.Strategies;

namespace SyncClipboard.Core.Utilities.Updater;

internal static class UpdateServiceRegistration
{
    public static IServiceCollection AddUpdateInstallation(this IServiceCollection services)
    {
        services.AddSingleton<UpdateTaskCleaner>();
        services.AddSingleton<UpdateTaskCoordinator>();
        services.AddSingleton<IUpdateInstallStrategy, WindowsExeInstaller>();
        services.AddSingleton<IUpdateInstallStrategy, WindowsZipInstaller>();
        services.AddSingleton<IUpdateInstallStrategy, MacDmgInstaller>();
        services.AddSingleton<IUpdateInstallStrategy, LinuxAppImageInstaller>();
        services.AddSingleton<IUpdateInstaller, UpdateInstallerDispatcher>();
        return services;
    }
}
