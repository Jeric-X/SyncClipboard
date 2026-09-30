using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Core.Utilities.Updater;

internal sealed class UnsupportedUpdateInstaller : IUpdateInstaller
{
    public bool RequiresAppExit => false;
    public UpdateInstallCapability GetCapability() => new(false);

    public Task StartAsync(UpdateInstallRequest request, CancellationToken token)
        => Task.FromException(new InvalidOperationException(I18n.Strings.UpdateLocationUnsupported));
}
