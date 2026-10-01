using SyncClipboard.Core.Interfaces;
using System.Diagnostics;

namespace SyncClipboard.Core.Utilities.Updater;

internal sealed class InnoSetupInstaller : IUpdateInstaller
{
    public async Task StartAsync(UpdateInstallRequest request, CancellationToken token)
    {
        var start = new ProcessStartInfo(Path.GetFullPath(request.PackagePath))
        {
            UseShellExecute = true
        };
        using var process = Process.Start(start) ?? throw new IOException("Could not start the installer.");
        await AppCore.Current.ExitAsync();
    }
}
