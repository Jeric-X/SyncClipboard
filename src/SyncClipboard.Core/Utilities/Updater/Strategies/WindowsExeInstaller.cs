using SyncClipboard.Core.Commons;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.PortableExecutable;

namespace SyncClipboard.Core.Utilities.Updater.Strategies;

internal sealed class WindowsExeInstaller : IUpdateInstallStrategy
{
    public UpdatePackageKind Kind => UpdatePackageKind.WindowsInstaller;

    // The interactive installer determines the destination and handles permissions itself.
    public UpdateInstallCapability GetCapability() => new(Kind, Path.TrimEndingDirectorySeparator(Env.ProgramDirectory));

    public Task<UpdateInstallTask> PrepareAsync(UpdateInstallRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ValidateExecutable(request.PackagePath);
        return Task.FromResult(new UpdateInstallTask
        {
            Directory = Path.GetDirectoryName(request.PackagePath)!,
            Kind = nameof(UpdatePackageKind.WindowsInstaller),
            Target = request.Capability.TargetPath,
            Stage = request.PackagePath,
            Backup = string.Empty,
            Executable = request.PackagePath,
            Version = request.Version,
            Digest = request.Digest,
            ProcessId = Environment.ProcessId
        });
    }

    public async Task StartAsync(UpdateInstallTask update, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var path = update.Executable;
        try
        {
            // Keep this exact file read-only through launch, including any authorization prompt.
            await using var package = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            await UpdatePackageVerifier.VerifyHashAsync(path, update.Digest, token);
            using var process = Process.Start(new ProcessStartInfo(path)
            {
                WorkingDirectory = Path.GetDirectoryName(path),
                UseShellExecute = true
            }) ?? throw new IOException("Could not start the update installer.");
            // The installer owns closing the app. If the user cancels, leave this app running and allow retry.
            await process.WaitForExitAsync(token);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("Installer authorization was canceled.", ex, token);
        }
    }

    private static void ValidateExecutable(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        if (reader.PEHeaders.PEHeader is null)
        {
            throw new InvalidDataException("The update installer is not a valid Windows executable.");
        }
    }
}
