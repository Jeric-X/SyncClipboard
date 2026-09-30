using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using System.Diagnostics;
using System.Globalization;

namespace SyncClipboard.Core.Utilities.Updater;

internal sealed class FileReplacementPackageInstaller(string updaterPath, string targetPath) : IUpdateInstaller
{
    public async Task StartAsync(UpdateInstallRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var process = Process.Start(CreateStartInfo(request)) ?? throw new IOException("Could not start the update helper.");
        await AppCore.Current.ExitAsync();
    }

    internal ProcessStartInfo CreateStartInfo(UpdateInstallRequest request)
    {
        var start = new ProcessStartInfo(Path.GetFullPath(updaterPath)) { UseShellExecute = false };
        // The updater waits for this process to exit, then owns staging, verification, replacement, and cleanup.
        string[] arguments =
        [
            "--package-path", Path.GetFullPath(request.PackagePath),
            "--digest", request.Digest,
            "--target", Path.GetFullPath(targetPath),
            "--executable", Env.ProgramPath,
            "--version", request.Version,
            "--process-id", Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
            "--language", CultureInfo.CurrentUICulture.Name
        ];
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }
}
