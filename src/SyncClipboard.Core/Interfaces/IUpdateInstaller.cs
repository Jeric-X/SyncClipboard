using SyncClipboard.Core.Utilities.Updater;

namespace SyncClipboard.Core.Interfaces;

public interface IUpdateInstaller
{
    // True when the caller must exit after StartAsync successfully hands off installation.
    bool RequiresAppExit { get; }
    UpdateInstallCapability GetCapability();
    Task<UpdateInstallTask> PrepareAsync(UpdateInstallRequest request, CancellationToken token);
    Task StartAsync(UpdateInstallTask update, CancellationToken token);
}
