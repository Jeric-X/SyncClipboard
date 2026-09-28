using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace SyncClipboard.Core.Utilities.Updater;

internal sealed class UpdateTaskCoordinator(UpdateTaskCleaner cleanup)
{
    public async Task<UpdateInstallTask> PrepareAsync(UpdateInstallRequest request, string parent,
        Func<UpdateInstallRequest, string, Task<UpdateInstallTask>> preparePayload, CancellationToken token)
    {
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
            var update = await preparePayload(request with { PackagePath = snapshot }, work);
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
            Directory.Delete(work, true);
            throw;
        }
    }

    public static async Task StartAsync(UpdateInstallTask update,
        Func<UpdateInstallTask, ProcessStartInfo> createStartInfo, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var start = createStartInfo(update);
            using var process = Process.Start(start) ?? throw new IOException("Could not start the update helper.");
            await WaitForReadyAsync(update, process, token);
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
        TimeSpan? readyTimeout = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(readyTimeout ?? TimeSpan.FromMinutes(5)); // Includes time for system authorization and staging.
        while (!File.Exists(Path.Combine(update.Directory, "ready")))
        {
            if (File.Exists(Path.Combine(update.Directory, "canceled"))) throw new OperationCanceledException();
            var error = ReadFailure(update.Directory);
            if (error is not null) throw new IOException(error);
            var helperExited = await HasHelperExitedAsync(update.Directory, token);
            if (helperExited || process.HasExited)
            {
                if (File.Exists(Path.Combine(update.Directory, "canceled"))) throw new OperationCanceledException();
                throw new IOException(ReadFailure(update.Directory) ?? "The update helper exited before it was ready.");
            }
            try { await Task.Delay(100, timeout.Token); }
            catch (OperationCanceledException ex) when (!token.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                throw new IOException("Timed out waiting for the update helper. Close its update or authorization window before retrying.", ex);
            }
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

    internal static async Task ExtractScriptAsync(string directory, string name, CancellationToken token)
    {
        await using var resource = typeof(UpdateTaskCoordinator).Assembly.GetManifestResourceStream(
            "SyncClipboard.Core.Utilities.Updater.Strategies.Scripts." + name)
            ?? throw new IOException("The embedded update script is missing: " + name);
        await using var output = File.Create(Path.Combine(directory, name));
        await resource.CopyToAsync(output, token);
    }
}
