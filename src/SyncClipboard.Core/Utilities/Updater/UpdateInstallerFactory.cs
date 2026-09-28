using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Updater.Strategies;

namespace SyncClipboard.Core.Utilities.Updater;

internal sealed class UpdateInstallerFactory(IServiceProvider services)
{
    public IUpdateInstaller Create(UpdateInfoConfig updateInfo)
        => Create(updateInfo, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), OperatingSystem.IsLinux());

    internal IUpdateInstaller Create(UpdateInfoConfig updateInfo, bool windows, bool mac, bool linux)
    {
        if (updateInfo.ManageType != UpdateInfoConfig.TypeManual || updateInfo.UpdateSrc != "github")
            return services.GetRequiredService<UnsupportedUpdateInstaller>();
        return GetPackageKind(updateInfo.PackageName, windows, mac, linux) switch
        {
            UpdatePackageKind.WindowsInstaller => services.GetRequiredService<WindowsExeInstaller>(),
            UpdatePackageKind.WindowsPortable => CreateFileReplacement<WindowsZipReplacementStrategy>(),
            UpdatePackageKind.MacBundle => CreateFileReplacement<MacDmgReplacementStrategy>(),
            UpdatePackageKind.AppImage => CreateFileReplacement<LinuxAppImageReplacementStrategy>(),
            _ => services.GetRequiredService<UnsupportedUpdateInstaller>()
        };
    }

    private FileReplacementUpdater CreateFileReplacement<T>() where T : class, IFileReplacementStrategy
        => new(services.GetRequiredService<T>(), services.GetRequiredService<UpdateTaskCleaner>());

    public static UpdatePackageKind GetPackageKind(string name, bool windows, bool mac, bool linux)
    {
        if (Path.GetFileName(name) != name || name.Contains('\\')) return UpdatePackageKind.Unsupported;
        if (windows && name.EndsWith("_installer.exe", StringComparison.OrdinalIgnoreCase)) return UpdatePackageKind.WindowsInstaller;
        if (windows && name.EndsWith("_portable.zip", StringComparison.OrdinalIgnoreCase)) return UpdatePackageKind.WindowsPortable;
        if (mac && name.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase)) return UpdatePackageKind.MacBundle;
        if (linux && name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase)) return UpdatePackageKind.AppImage;
        return UpdatePackageKind.Unsupported;
    }
}
