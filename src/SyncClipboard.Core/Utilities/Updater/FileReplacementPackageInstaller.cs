using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using System.Diagnostics;
using System.Globalization;

namespace SyncClipboard.Core.Utilities.Updater;

internal sealed class FileReplacementPackageInstaller : IUpdateInstaller
{
    public async Task StartAsync(UpdateInstallRequest request, CancellationToken token)
    {
        var bundle = OperatingSystem.IsMacOS() ? MacUpdaterFiles.FindBundle(Env.ProgramDirectory) : null;
        var workspace = bundle is null
            ? await PrepareUpdaterAsync(Path.Combine(Env.ProgramDirectory, "SyncClipboard.Updater.exe"), token)
            : await PrepareMacUpdaterAsync(bundle, token);
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

    internal static async Task<string> PrepareUpdaterAsync(string updaterPath, CancellationToken token,
        string fileName = "SyncClipboard.Updater.exe")
    {
        var workspace = Path.Combine(Path.GetTempPath(), "SyncClipboard-updates", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workspace);
            await File.WriteAllTextAsync(Path.Combine(workspace, ".syncclipboard-update"), "SyncClipboard updater workspace v1", token);
            await using var source = File.OpenRead(updaterPath);
            await using var destination = new FileStream(Path.Combine(workspace, fileName),
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

    internal static async Task<string> PrepareMacUpdaterAsync(string bundle, CancellationToken token)
    {
        var files = MacUpdaterFiles.GetFiles(bundle);
        if (files.Length == 0)
            throw new FileNotFoundException("The macOS updater or its native libraries are missing.");
        var workspace = await PrepareUpdaterAsync(files[0], token, "SyncClipboard.Updater");
        try
        {
            var executable = Path.Combine(workspace, "SyncClipboard.Updater");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(executable, File.GetUnixFileMode(files[0]) | UnixFileMode.UserExecute);
            foreach (var library in files.Skip(1))
            {
                await using var source = File.OpenRead(library);
                await using var destination = File.Create(Path.Combine(workspace, Path.GetFileName(library)));
                await source.CopyToAsync(destination, token);
            }
            return workspace;
        }
        catch
        {
            Directory.Delete(workspace, true);
            throw;
        }
    }

    internal static ProcessStartInfo CreateStartInfo(UpdateInstallRequest request, string workspace, string? programDirectory = null)
    {
        programDirectory ??= Env.ProgramDirectory;
        var targetPath = OperatingSystem.IsMacOS()
            ? MacUpdaterFiles.FindBundle(programDirectory) ?? Path.GetFullPath(programDirectory)
            : Path.GetFullPath(programDirectory);
        var updaterPath = Path.Combine(workspace, OperatingSystem.IsMacOS() ? "SyncClipboard.Updater" : "SyncClipboard.Updater.exe");
        var appElevated = OperatingSystem.IsWindows() && Env.IsRunningAsAdministrator;
        using var currentProcess = Process.GetCurrentProcess();
        var start = new ProcessStartInfo(updaterPath) { UseShellExecute = false };
        // The updater waits for this process to exit, then owns staging, verification, replacement, and cleanup.
        string[] arguments =
        [
            "--work-dir", workspace,
            "--package-path", Path.GetFullPath(request.PackagePath),
            "--digest", request.Digest,
            "--target", targetPath,
            "--process-id", Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
            "--process-start-time", currentProcess.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            "--app-elevated", appElevated ? "true" : "false",
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
