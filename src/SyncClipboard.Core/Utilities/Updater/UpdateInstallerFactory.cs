using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Utilities.Updater;

internal sealed class UpdateInstallerFactory : IUpdateInstallerFactory
{
    public IUpdateInstaller? Create(UpdateInfoConfig updateInfo)
        => Create(updateInfo, OperatingSystem.IsWindows());

    internal static IUpdateInstaller? Create(UpdateInfoConfig updateInfo, bool windows)
    {
        if (updateInfo.ManageType != UpdateInfoConfig.TypeManual || updateInfo.UpdateSrc != "github"
            || string.IsNullOrWhiteSpace(updateInfo.PackageName)) return null;

        var name = updateInfo.PackageName;
        if (windows && Path.GetFileName(name) == name && !name.Contains('\\')
            && name.StartsWith("SyncClipboard_win_", StringComparison.Ordinal)
            && name.EndsWith("_portable.zip", StringComparison.OrdinalIgnoreCase))
        {
            return new FileReplacementPackageInstaller(Path.Combine(Env.ProgramDirectory, "SyncClipboard.Updater.exe"),
                Env.ProgramDirectory);
        }
        return null;
    }
}
