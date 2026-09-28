namespace SyncClipboard.Core.Utilities.Updater.Strategies;

internal interface IUpdateInstallStrategy
{
    UpdatePackageKind Kind { get; }
    UpdateInstallCapability GetCapability();
    Task<UpdateInstallTask> PrepareAsync(UpdateInstallRequest request, CancellationToken token);
    Task StartAsync(UpdateInstallTask update, CancellationToken token);
}
