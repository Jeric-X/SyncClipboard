using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using System.Diagnostics;
using System.Security.Cryptography;

namespace SyncClipboard.Core.Utilities.Updater;

internal sealed class InnoSetupInstaller : IUpdateInstaller
{
    public async Task StartAsync(UpdateInstallRequest request, CancellationToken token)
    {
        AppInstance.CheckCanInstallUpdate();
        await using var file = new FileStream(request.PackagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var digest = "sha256:" + Convert.ToHexString(await SHA256.HashDataAsync(file, token));
        if (!string.Equals(digest, request.Digest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(I18n.Strings.HashMismatch);
        var start = CreateStartInfo(request.PackagePath, AppLaunchOptions.Current);
        using var process = Process.Start(start) ?? throw new IOException("Could not start the installer.");
        await AppCore.Current.ExitAsync();
    }

    internal static ProcessStartInfo CreateStartInfo(string packagePath, AppLaunchOptions options)
    {
        var start = new ProcessStartInfo(Path.GetFullPath(packagePath)) { UseShellExecute = true };
        if (options.AppDataDirectory is string directory)
        {
            // A final dot avoids a trailing backslash before a closing command-line quote.
            start.ArgumentList.Add("/APPDATADIR=" + Path.Combine(directory, "."));
        }
        return start;
    }
}
