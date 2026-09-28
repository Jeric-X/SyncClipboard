using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities.Updater.Strategies;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace SyncClipboard.Core.Utilities.Updater;

internal sealed class FileReplacementUpdater(IFileReplacementStrategy strategy, UpdateTaskCleaner cleanup) : IUpdateInstaller
{
    public bool RequiresAppExit => true;

    public UpdateInstallCapability GetCapability() => strategy.GetCapability();

    public Task<UpdateInstallTask> PrepareAsync(UpdateInstallRequest request, CancellationToken token)
        => Task.Run(() => PrepareCoreAsync(request, token), token);

    private async Task<UpdateInstallTask> PrepareCoreAsync(UpdateInstallRequest request, CancellationToken token)
    {
        if (request.Capability.Kind != strategy.Kind) throw new InvalidOperationException("The update does not match the selected strategy.");
        await UpdatePackageVerifier.VerifyHashAsync(request.PackagePath, request.Digest, token);
        var parent = strategy.GetInstallationDirectory(request.Capability.TargetPath);
        EnsurePreviousHelperStopped(cleanup.TaskDirectory);
        var work = Path.Combine(cleanup.TaskDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var size = new FileInfo(request.PackagePath).Length;
            UpdateFileSystem.CheckSpace(work, checked(size * 4));
            UpdateFileSystem.CheckSpace(parent, checked(size * 4));
            var download = Path.Combine(work, "download");
            Directory.CreateDirectory(download);
            var snapshot = Path.Combine(download, Path.GetFileName(request.PackagePath));
            File.Copy(request.PackagePath, snapshot);
            await UpdatePackageVerifier.VerifyHashAsync(snapshot, request.Digest, token);
            var update = await strategy.PreparePayloadAsync(request with { PackagePath = snapshot }, work, token);
            update = update with { Language = CultureInfo.CurrentUICulture.Name };
            var stagedSize = Directory.EnumerateFiles(update.Stage, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            }).Sum(path => new FileInfo(path).Length);
            UpdateFileSystem.CheckSpace(parent, stagedSize);
            token.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(Path.Combine(work, "task.json"), JsonSerializer.Serialize(update), token);
            return update;
        }
        catch
        {
            if (strategy.CanRemoveFailedPreparation(work)) Directory.Delete(work, true);
            throw;
        }
    }

    public async Task StartAsync(UpdateInstallTask update, CancellationToken token)
    {
        if (update.Kind != strategy.Kind.ToString()) throw new InvalidOperationException("The update does not match the selected strategy.");
        token.ThrowIfCancellationRequested();
        try
        {
            var start = strategy.CreateWorkerStartInfo(update);
            using var process = Process.Start(start) ?? throw new IOException("Could not start the update helper.");
            await WaitForReadyAsync(update, process, token, launcherMayExit: strategy.LauncherMayExit);
            token.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(Path.Combine(update.Directory, "commit"), string.Empty, token);
        }
        catch
        {
            await File.WriteAllTextAsync(Path.Combine(update.Directory, "cancel"), string.Empty, CancellationToken.None);
            throw;
        }
    }

    internal static void EnsurePreviousHelperStopped(string taskDirectory)
    {
        if (!Directory.Exists(taskDirectory)) return;
        foreach (var work in Directory.EnumerateDirectories(taskDirectory))
        {
            if (File.Exists(Path.Combine(work, "cancel")) && UpdateTaskCleaner.HelpersRunning(work))
                throw new IOException("The previous update helper is still running. Close its update or authorization window before retrying.");
        }
    }

    internal static async Task WaitForReadyAsync(UpdateInstallTask update, Process process, CancellationToken token,
        TimeSpan? readyTimeout = null, bool launcherMayExit = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(readyTimeout ?? TimeSpan.FromMinutes(5)); // Includes time for system authorization and staging.
        while (!File.Exists(Path.Combine(update.Directory, "ready")))
        {
            await ThrowIfHelperFailedAsync(update.Directory, process, launcherMayExit, token);
            try { await Task.Delay(100, timeout.Token); }
            catch (OperationCanceledException ex) when (!token.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                throw new IOException("Timed out waiting for the update helper. Close its update or authorization window before retrying.", ex);
            }
        }
    }

    private static async Task ThrowIfHelperFailedAsync(string directory, Process process, bool launcherMayExit,
        CancellationToken token)
    {
        if (File.Exists(Path.Combine(directory, "canceled"))) throw new OperationCanceledException();
        var error = ReadFailure(directory);
        if (error is not null) throw new IOException(error);
        var helperExited = await HasHelperExitedAsync(directory, token);
        if (helperExited || (process.HasExited && (!launcherMayExit || process.ExitCode != 0)))
        {
            if (File.Exists(Path.Combine(directory, "canceled"))) throw new OperationCanceledException();
            throw new IOException(ReadFailure(directory) ?? "The update helper exited before it was ready.");
        }
    }

    private static async Task<bool> HasHelperExitedAsync(string directory, CancellationToken token)
    {
        var pidPath = Path.Combine(directory, "helper-pid");
        return File.Exists(pidPath) && int.TryParse(await File.ReadAllTextAsync(pidPath, token), out var pid)
            && !UpdateTaskCleaner.IsProcessRunning(pid);
    }

    internal static string? ReadFailure(string work) => File.Exists(Path.Combine(work, "failed"))
        ? File.ReadAllText(Path.Combine(work, "failed")) : null;
}
