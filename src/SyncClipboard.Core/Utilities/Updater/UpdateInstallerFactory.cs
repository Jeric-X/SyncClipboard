using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Utilities.Updater;

internal sealed class UpdateInstallerFactory : IUpdateInstallerFactory
{
    public IUpdateInstaller? Create(UpdateInfoConfig updateInfo)
        => Create(updateInfo, OperatingSystem.IsWindows(), Env.ProgramDirectory, OperatingSystem.IsMacOS(),
            OperatingSystem.IsLinux() ? Env.GetAppImageExecPath() : null);

    internal static IUpdateInstaller? Create(UpdateInfoConfig updateInfo, bool isWindows, string programDirectory,
        bool isMacOS = false, string? appImagePath = null)
    {
        if (updateInfo.ManageType != UpdateInfoConfig.TypeManual || updateInfo.UpdateSrc != "github"
            || string.IsNullOrWhiteSpace(updateInfo.PackageName))
            return null;

        if (isWindows && updateInfo.PackageName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return File.Exists(Path.Combine(programDirectory, "SyncClipboard.Updater.exe"))
                ? new FileReplacementPackageInstaller() : null;

        if (isMacOS && updateInfo.PackageName.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase))
        {
            var bundle = MacUpdaterFiles.FindBundle(programDirectory);
            if (bundle is not null && MacUpdaterFiles.GetFiles(bundle).Length != 0)
                return new FileReplacementPackageInstaller();
        }
        if (!isWindows && !isMacOS && updateInfo.PackageName.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase)
            && appImagePath is not null && Path.IsPathFullyQualified(appImagePath) && File.Exists(appImagePath)
            && LinuxUpdaterFiles.GetFiles(programDirectory).Length != 0)
            return new FileReplacementPackageInstaller();
        return null;
    }
}
