using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Updater;

namespace SyncClipboard.Core.Interfaces;

public interface IUpdateInstaller
{
    UpdateInstallCapability GetCapability(UpdateInfoConfig updateInfo);
    Task<PreparedUpdate> PrepareAsync(UpdateInstallRequest request, CancellationToken token);
    Task StartAsync(PreparedUpdate update, CancellationToken token);
    Task CleanupCompletedAsync();
}
