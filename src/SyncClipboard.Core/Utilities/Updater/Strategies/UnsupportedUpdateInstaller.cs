using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Core.Utilities.Updater.Strategies;

internal sealed class UnsupportedUpdateInstaller : IUpdateInstaller
{
    public bool RequiresAppExit => false;
    public UpdateInstallCapability GetCapability() => new(UpdatePackageKind.Unsupported, string.Empty);

    public Task<UpdateInstallTask> PrepareAsync(UpdateInstallRequest request, CancellationToken token)
        => Task.FromException<UpdateInstallTask>(new InvalidOperationException(I18n.Strings.UpdateLocationUnsupported));

    public Task StartAsync(UpdateInstallTask update, CancellationToken token)
        => Task.FromException(new InvalidOperationException(I18n.Strings.UpdateLocationUnsupported));
}
