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

        if (isWindows)
            return CreateWindowsInstaller(updateInfo.PackageName, programDirectory);
        if (isMacOS)
            return CreateMacInstaller(updateInfo.PackageName, programDirectory);
        return CreateAppImageInstaller(updateInfo.PackageName, programDirectory, appImagePath);
    }

    private static IUpdateInstaller? CreateWindowsInstaller(string packageName, string programDirectory)
    {
        if (packageName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return new InnoSetupInstaller();
        if (packageName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            && File.Exists(Path.Combine(programDirectory, "SyncClipboard.Updater.exe")))
            return new FileReplacementPackageInstaller();
        return null;
    }

    private static FileReplacementPackageInstaller? CreateMacInstaller(string packageName, string programDirectory)
    {
        if (!packageName.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase))
            return null;
        var bundle = MacUpdaterFiles.FindBundle(programDirectory);
        return bundle is not null && MacUpdaterFiles.GetFiles(bundle).Length != 0
            ? new FileReplacementPackageInstaller() : null;
    }

    private static FileReplacementPackageInstaller? CreateAppImageInstaller(string packageName, string programDirectory, string? appImagePath)
    {
        if (!packageName.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase)
            || appImagePath is null || !Path.IsPathFullyQualified(appImagePath) || !File.Exists(appImagePath))
            return null;
        return FileReplacementPackageInstaller.LinuxUpdaterFiles.GetFiles(programDirectory).Length != 0
            ? new FileReplacementPackageInstaller() : null;
    }
}
