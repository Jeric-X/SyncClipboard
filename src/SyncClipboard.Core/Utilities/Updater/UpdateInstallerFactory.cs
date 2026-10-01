using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Utilities.Updater;

internal sealed class UpdateInstallerFactory : IUpdateInstallerFactory
{
    public IUpdateInstaller? Create(UpdateInfoConfig updateInfo)
        => Create(updateInfo, OperatingSystem.IsWindows(), Env.ProgramDirectory);

    internal static IUpdateInstaller? Create(UpdateInfoConfig updateInfo, bool isWindows, string programDirectory)
    {
        if (updateInfo.ManageType != UpdateInfoConfig.TypeManual || updateInfo.UpdateSrc != "github"
            || string.IsNullOrWhiteSpace(updateInfo.PackageName))
            return null;

        if (!isWindows || !updateInfo.PackageName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!File.Exists(Path.Combine(programDirectory, "SyncClipboard.Updater.exe")))
            return null;

        return new FileReplacementPackageInstaller();
    }
}
