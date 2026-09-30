using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Utilities.Updater;

internal sealed class UpdateInstallerFactory : IUpdateInstallerFactory
{
    public IUpdateInstaller? Create(UpdateInfoConfig updateInfo)
        => Create(updateInfo, OperatingSystem.IsWindows());

    internal static IUpdateInstaller? Create(UpdateInfoConfig updateInfo, bool isWindows)
    {
        if (updateInfo.ManageType != UpdateInfoConfig.TypeManual || updateInfo.UpdateSrc != "github"
            || string.IsNullOrWhiteSpace(updateInfo.PackageName))
            return null;

        if (isWindows && updateInfo.PackageName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return new FileReplacementPackageInstaller();
        return null;
    }
}
