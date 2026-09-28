using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Updater.Strategies;

namespace SyncClipboard.Core.Utilities.Updater;

internal static class UpdateServiceRegistration
{
    public static IServiceCollection AddUpdateInstallation(this IServiceCollection services)
    {
        services.AddSingleton<UpdateTaskCleaner>();
        services.AddSingleton<UpdateInstallerFactory>();
        services.AddSingleton<UnsupportedUpdateInstaller>();
        services.AddSingleton<WindowsExeInstaller>();
        services.AddSingleton<WindowsZipReplacementStrategy>();
        services.AddSingleton<MacDmgReplacementStrategy>();
        services.AddSingleton<LinuxAppImageReplacementStrategy>();
        services.AddSingleton<IUpdateInstaller>(provider => provider.GetRequiredService<UpdateInstallerFactory>().Create(
            provider.GetRequiredKeyedService<ConfigBase>(Env.UpdateInfoFile).GetConfig<UpdateInfoConfig>()));
        return services;
    }
}
