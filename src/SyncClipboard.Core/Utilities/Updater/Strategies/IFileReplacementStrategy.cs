using System.Diagnostics;

namespace SyncClipboard.Core.Utilities.Updater.Strategies;

internal interface IFileReplacementStrategy
{
    UpdatePackageKind Kind { get; }
    UpdateInstallCapability GetCapability();
    string GetInstallationDirectory(string target);
    Task<UpdateInstallTask> PreparePayloadAsync(UpdateInstallRequest request, string work, CancellationToken token);
    ProcessStartInfo CreateWorkerStartInfo(UpdateInstallTask update);
    bool LauncherMayExit => false;
    bool CanRemoveFailedPreparation(string work) => true;
}
