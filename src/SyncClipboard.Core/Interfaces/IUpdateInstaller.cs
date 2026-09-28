using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Updater;

namespace SyncClipboard.Core.Interfaces;

public interface IUpdateInstaller
{
    UpdateInstallCapability GetCapability(UpdateInfoConfig updateInfo);
    Task<UpdateInstallTask> PrepareAsync(UpdateInstallRequest request, CancellationToken token);
    Task StartAsync(UpdateInstallTask update, CancellationToken token);
    Task CleanupCompletedAsync();
}
