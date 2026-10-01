using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using System.Diagnostics;
using System.Globalization;

namespace SyncClipboard.Core.Utilities.Updater;

internal sealed class FileReplacementPackageInstaller : IUpdateInstaller
{
    public async Task StartAsync(UpdateInstallRequest request, CancellationToken token)
    {
        var workspace = await PrepareUpdaterAsync(Path.Combine(Env.ProgramDirectory, "SyncClipboard.Updater.exe"), token);
        var started = false;
        try
        {
            using var process = Process.Start(CreateStartInfo(request, workspace))
                ?? throw new IOException("Could not start the update helper.");
            started = true;
            await AppCore.Current.ExitAsync();
        }
        finally
        {
            if (!started)
                Directory.Delete(workspace, true);
        }
    }

    internal static async Task<string> PrepareUpdaterAsync(string updaterPath, CancellationToken token)
    {
        var workspace = Path.Combine(Path.GetTempPath(), "SyncClipboard-updates", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workspace);
            await File.WriteAllTextAsync(Path.Combine(workspace, ".syncclipboard-update"), "SyncClipboard updater workspace v1", token);
            await using var source = File.OpenRead(updaterPath);
            await using var destination = new FileStream(Path.Combine(workspace, "SyncClipboard.Updater.exe"),
                FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(destination, token);
            return workspace;
        }
        catch
        {
            if (Directory.Exists(workspace))
                Directory.Delete(workspace, true);
            throw;
        }
    }

    internal static ProcessStartInfo CreateStartInfo(UpdateInstallRequest request, string workspace)
    {
        var targetPath = Path.GetFullPath(Env.ProgramDirectory);
        var updaterPath = Path.Combine(workspace, "SyncClipboard.Updater.exe");
        using var currentProcess = Process.GetCurrentProcess();
        var start = new ProcessStartInfo(updaterPath) { UseShellExecute = false };
        // The updater waits for this process to exit, then owns staging, verification, replacement, and cleanup.
        string[] arguments =
        [
            "--work-dir", workspace,
            "--package-path", Path.GetFullPath(request.PackagePath),
            "--digest", request.Digest,
            "--target", targetPath,
            "--executable", OperatingSystem.IsWindows() ? Path.Combine(targetPath, "SyncClipboard.exe") : Env.ProgramPath,
            "--process-id", Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
            "--process-start-time", currentProcess.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            "--language", CultureInfo.CurrentUICulture.Name
        ];
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        string[] protectedPaths =
        [
            Env.AppDataDirectory,
            Env.StaticConfigPath,
            Env.PortableUserConfigFile,
            Env.PortableAppDataDirectory,
            Env.RuntimeConfigPath,
            Env.AppDataPathConfigPath
        ];
        foreach (var path in protectedPaths)
        {
            start.ArgumentList.Add("--protect-path");
            start.ArgumentList.Add(Path.GetFullPath(path));
        }
        return start;
    }
}
