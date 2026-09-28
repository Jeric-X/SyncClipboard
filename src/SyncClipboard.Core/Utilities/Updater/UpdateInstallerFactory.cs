using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Updater.Strategies;

namespace SyncClipboard.Core.Utilities.Updater;

internal sealed class UpdateInstallerFactory(IServiceProvider services)
{
    public IUpdateInstaller Create(UpdateInfoConfig updateInfo)
        => Create(updateInfo, OperatingSystem.IsWindows());

    internal IUpdateInstaller Create(UpdateInfoConfig updateInfo, bool windows)
    {
        if (updateInfo.ManageType != UpdateInfoConfig.TypeManual || updateInfo.UpdateSrc != "github")
            return services.GetRequiredService<UnsupportedUpdateInstaller>();
        return GetPackageKind(updateInfo.PackageName, windows) switch
        {
            UpdatePackageKind.WindowsPortable => CreateFileReplacement<WindowsZipReplacementStrategy>(),
            _ => services.GetRequiredService<UnsupportedUpdateInstaller>()
        };
    }

    private FileReplacementUpdater CreateFileReplacement<T>() where T : class, IFileReplacementStrategy
        => new(services.GetRequiredService<T>(), services.GetRequiredService<UpdateTaskCleaner>());

    public static UpdatePackageKind GetPackageKind(string name, bool windows)
    {
        if (Path.GetFileName(name) != name || name.Contains('\\')) return UpdatePackageKind.Unsupported;
        if (windows && name.EndsWith("_portable.zip", StringComparison.OrdinalIgnoreCase)) return UpdatePackageKind.WindowsPortable;
        return UpdatePackageKind.Unsupported;
    }
}
