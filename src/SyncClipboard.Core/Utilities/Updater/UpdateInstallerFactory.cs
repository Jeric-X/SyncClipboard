using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Utilities.Updater;

internal sealed class UpdateInstallerFactory : IUpdateInstallerFactory
{
    private readonly UnsupportedUpdateInstaller unsupported = new();
    private readonly List<(Func<UpdateInfoConfig, bool> Matches, Func<UpdateInfoConfig, IUpdateInstaller> Create)> rules = [];

    // Register implemented package handlers at startup, before UpdateChecker is resolved.
    public void Register(Func<UpdateInfoConfig, bool> matches, Func<UpdateInfoConfig, IUpdateInstaller> create)
        => rules.Add((matches, create));

    public IUpdateInstaller Create(UpdateInfoConfig updateInfo)
    {
        if (updateInfo.ManageType != UpdateInfoConfig.TypeManual || updateInfo.UpdateSrc != "github"
            || string.IsNullOrWhiteSpace(updateInfo.PackageName)) return unsupported;

        foreach (var rule in rules)
        {
            if (rule.Matches(updateInfo)) return rule.Create(updateInfo);
        }
        return unsupported;
    }
}
