using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Interfaces;

public interface IUpdateInstallerFactory
{
    IUpdateInstaller? Create(UpdateInfoConfig updateInfo);
}
