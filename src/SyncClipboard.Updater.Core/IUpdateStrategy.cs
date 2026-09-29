namespace SyncClipboard.Updater.Core;

public interface IUpdateStrategy
{
    UpdatePackageKind PackageKind { get; }
    Task ExecuteAsync(UpdateRequest request, IProgress<UpdateProgress> progress, CancellationToken token);
}
