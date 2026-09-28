using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace SyncClipboard.Core.Utilities.Updater;

internal sealed class UpdateInstallHelper(UpdateInstallCleanup cleanup)
{
    public async Task<PreparedUpdate> PrepareAsync(UpdateInstallRequest request, string parent,
        Func<UpdateInstallRequest, string, Task<PreparedUpdate>> preparePayload, CancellationToken token)
    {
        var work = Path.Combine(cleanup.TaskDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(work, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            var size = new FileInfo(request.PackagePath).Length;
            UpdateInstallFiles.CheckSpace(work, checked(size * 4));
            UpdateInstallFiles.CheckSpace(parent, checked(size * 4));
            var download = Path.Combine(work, "download");
            Directory.CreateDirectory(download);
            var snapshot = Path.Combine(download, Path.GetFileName(request.PackagePath));
            File.Copy(request.PackagePath, snapshot);
            await UpdateInstallFiles.VerifyHashAsync(snapshot, request.Digest, token);
            var update = await preparePayload(request with { PackagePath = snapshot }, work);
            update = update with { Digest = request.Digest, Language = CultureInfo.CurrentUICulture.Name };
            var stagedSize = Directory.Exists(update.Stage)
                ? Directory.EnumerateFiles(update.Stage, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                }).Sum(path => new FileInfo(path).Length)
                : new FileInfo(update.Stage).Length;
            UpdateInstallFiles.CheckSpace(parent, stagedSize);
            token.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(Path.Combine(work, "task.json"), JsonSerializer.Serialize(update), token);
            foreach (var name in new[] { "install.sh", "install.command", "install.ps1", "elevate.applescript" })
            {
                await using var resource = typeof(UpdateInstaller).Assembly.GetManifestResourceStream(
                    "SyncClipboard.Core.Utilities.Updater.Scripts." + name)!;
                await using var output = File.Create(Path.Combine(work, name));
                await resource.CopyToAsync(output, token);
            }
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(work, "install.command"),
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            // Shell reads separate UTF-8 values as data, never sources generated shell code.
            foreach (var (key, value) in new Dictionary<string, string>
            {
                ["kind"] = update.Kind,
                ["language"] = update.Language,
                ["target"] = update.Target,
                ["stage"] = update.Stage,
                ["backup"] = update.Backup,
                ["executable"] = update.Executable,
                ["pid"] = update.ProcessId.ToString(),
                ["elevate"] = update.Elevate ? "yes" : "no"
            })
            {
                await File.WriteAllTextAsync(Path.Combine(work, key + ".txt"), value, token);
            }
            return update;
        }
        catch
        {
            // A failed detach may leave a read-only DMG mounted here. Do not traverse it during cleanup.
            if (!Directory.Exists(Path.Combine(work, "mount"))) Directory.Delete(work, true);
            throw;
        }
    }

    public static async Task StartAsync(PreparedUpdate update, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var start = CreateStartInfo(update);
            using var process = Process.Start(start) ?? throw new IOException("Could not start the update helper.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(5)); // Includes time for system authorization and staging.
            while (!File.Exists(Path.Combine(update.Directory, "ready")))
            {
                if (File.Exists(Path.Combine(update.Directory, "canceled"))) throw new OperationCanceledException();
                var error = ReadFailure(update.Directory);
                if (error is not null) throw new IOException(error);
                var pidPath = Path.Combine(update.Directory, "helper-pid");
                var helperExited = File.Exists(pidPath) && int.TryParse(await File.ReadAllTextAsync(pidPath, token), out var pid)
                    && !UpdateInstallCleanup.IsProcessRunning(pid);
                if (helperExited || (process.HasExited && (update.Kind != nameof(UpdatePackageKind.MacBundle) || process.ExitCode != 0)))
                {
                    if (File.Exists(Path.Combine(update.Directory, "canceled"))) throw new OperationCanceledException();
                    throw new IOException(ReadFailure(update.Directory) ?? "The update helper exited before it was ready.");
                }
                await Task.Delay(100, timeout.Token);
            }
            token.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(Path.Combine(update.Directory, "commit"), string.Empty, token);
        }
        catch
        {
            await File.WriteAllTextAsync(Path.Combine(update.Directory, "cancel"), string.Empty, CancellationToken.None);
            throw;
        }
    }

    internal static string? ReadFailure(string work) => File.Exists(Path.Combine(work, "failed"))
        ? File.ReadAllText(Path.Combine(work, "failed")) : null;

    internal static ProcessStartInfo CreateStartInfo(PreparedUpdate update)
    {
        if (update.Kind == nameof(UpdatePackageKind.AppImage))
        {
            var start = AppImageUpdateRunner.CreateLaunchInfo(update.HelperExecutable
                ?? throw new IOException("The AppImage update helper is missing."));
            start.ArgumentList.Add("--install-update");
            start.ArgumentList.Add(Path.Combine(update.Directory, "task.json"));
            return start;
        }
        if (update.Kind == nameof(UpdatePackageKind.MacBundle))
        {
            var start = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
            foreach (var argument in new[] { "-a", "Terminal", Path.Combine(update.Directory, "install.terminal") })
                start.ArgumentList.Add(argument);
            return start;
        }
        var windows = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = true, WorkingDirectory = update.Directory };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(update.Directory, "install.ps1"), "-Work", update.Directory }) windows.ArgumentList.Add(argument);
        return windows;
    }
}
