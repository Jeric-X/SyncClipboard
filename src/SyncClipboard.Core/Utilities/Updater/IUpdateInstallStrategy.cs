namespace SyncClipboard.Core.Utilities.Updater;

internal interface IUpdateInstallStrategy
{
    UpdatePackageKind Kind { get; }
    UpdateInstallCapability GetCapability();
    Task<PreparedUpdate> PrepareAsync(UpdateInstallRequest request, CancellationToken token);
    Task StartAsync(PreparedUpdate update, CancellationToken token);
}
