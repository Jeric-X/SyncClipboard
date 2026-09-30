using System.Diagnostics;

namespace SyncClipboard.Core.Utilities.Updater.Strategies;

internal interface IFileReplacementStrategy
{
    UpdatePackageKind Kind { get; }
    UpdateInstallCapability GetCapability();
    string GetInstallationDirectory(string target);
    Task<UpdateInstallTask> PreparePayloadAsync(UpdateInstallRequest request, string work, CancellationToken token);
    // Whole-target replacements remove the previous payload before copying. Overlay strategies override this.
    long GetRequiredInstallationSpace(UpdateInstallTask update)
        => Math.Max(0, UpdateFileSystem.GetSize(update.Stage) - UpdateFileSystem.GetSize(update.Target));
    // The worker owns replacement, restart, and successful-update cleanup. It must retain rollback files
    // until replacement and restart succeed, and preserve backups/logs on failure or cancellation.
    ProcessStartInfo CreateWorkerStartInfo(UpdateInstallTask update);
    bool LauncherMayExit => false;
    bool CanRemoveFailedPreparation(string work) => true;
}
