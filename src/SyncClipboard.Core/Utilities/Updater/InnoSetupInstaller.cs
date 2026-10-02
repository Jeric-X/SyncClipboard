using SyncClipboard.Core.Interfaces;
using System.Diagnostics;
using System.Security.Cryptography;

namespace SyncClipboard.Core.Utilities.Updater;

internal sealed class InnoSetupInstaller : IUpdateInstaller
{
    public async Task StartAsync(UpdateInstallRequest request, CancellationToken token)
    {
        await using var file = new FileStream(request.PackagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var digest = "sha256:" + Convert.ToHexString(await SHA256.HashDataAsync(file, token));
        if (!string.Equals(digest, request.Digest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(I18n.Strings.HashMismatch);
        var start = new ProcessStartInfo(Path.GetFullPath(request.PackagePath))
        {
            UseShellExecute = true
        };
        using var process = Process.Start(start) ?? throw new IOException("Could not start the installer.");
        await AppCore.Current.ExitAsync();
    }
}
