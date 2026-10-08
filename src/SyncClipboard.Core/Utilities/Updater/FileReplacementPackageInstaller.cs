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
        string workspace;
        if (OperatingSystem.IsLinux())
            workspace = await PrepareUnixUpdaterAsync(LinuxUpdaterFiles.GetFiles(Env.ProgramDirectory), token);
        else if (bundle is not null)
            workspace = await PrepareMacUpdaterAsync(bundle, token);
        else
            workspace = await PrepareWindowsUpdaterAsync(Env.ProgramDirectory, token);
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

    internal static async Task<string> PrepareUpdaterAsync(IEnumerable<(string Source, string Name)> files,
        CancellationToken token)
    {
        var workspace = OperatingSystem.IsLinux()
            ? Directory.CreateTempSubdirectory("SyncClipboard-update-").FullName
            : Path.Combine(Path.GetTempPath(), "SyncClipboard-updates", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workspace);
            await File.WriteAllTextAsync(Path.Combine(workspace, ".syncclipboard-update"), "SyncClipboard updater workspace v1", token);
            foreach (var (path, name) in files)
            {
                await using var source = File.OpenRead(path);
                await using var destination = new FileStream(Path.Combine(workspace, name),
                    FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await source.CopyToAsync(destination, token);
            }
            return workspace;
        }
        catch
        {
            if (Directory.Exists(workspace))
                Directory.Delete(workspace, true);
            throw;
        }
    }

    internal static Task<string> PrepareWindowsUpdaterAsync(string directory, CancellationToken token)
        => PrepareUpdaterAsync(WindowsUpdaterFiles.GetFiles(directory), token);

    internal static Task<string> PrepareMacUpdaterAsync(string bundle, CancellationToken token)
        => PrepareUnixUpdaterAsync(MacUpdaterFiles.GetFiles(bundle), token);

    internal static async Task<string> PrepareUnixUpdaterAsync(string[] files, CancellationToken token)
    {
        if (files.Length == 0)
            throw new FileNotFoundException("The updater or its native libraries are missing.");
        var workspace = await PrepareUpdaterAsync(files.Select(path => (path, Path.GetFileName(path))), token);
        try
        {
            var executable = Path.Combine(workspace, "SyncClipboard.Updater");
            if (!OperatingSystem.IsWindows())
            {
                foreach (var file in files)
                {
                    File.SetUnixFileMode(Path.Combine(workspace, Path.GetFileName(file)), File.GetUnixFileMode(file));
                }
                File.SetUnixFileMode(executable, File.GetUnixFileMode(files[0]) | UnixFileMode.UserExecute);
            }
            return workspace;
        }
        catch
        {
            Directory.Delete(workspace, true);
            throw;
        }
    }

    internal static ProcessStartInfo CreateStartInfo(UpdateInstallRequest request, string workspace,
        string? programDirectory = null, string? appImagePath = null)
    {
        programDirectory ??= Env.ProgramDirectory;
        var targetPath = Path.GetFullPath(programDirectory);
        if (OperatingSystem.IsMacOS())
            targetPath = MacUpdaterFiles.FindBundle(programDirectory) ?? targetPath;
        else if (OperatingSystem.IsLinux())
            targetPath = appImagePath ?? Env.GetAppImageExecPath()
                ?? throw new InvalidOperationException("The running AppImage could not be located.");
        var updaterPath = Path.Combine(workspace, OperatingSystem.IsWindows() ? "SyncClipboard.Updater.exe" : "SyncClipboard.Updater");
        var appElevated = OperatingSystem.IsWindows() && Env.IsRunningAsAdministrator;
        using var currentProcess = Process.GetCurrentProcess();
        var start = new ProcessStartInfo(updaterPath) { UseShellExecute = false, WorkingDirectory = workspace };
        if (OperatingSystem.IsLinux())
            LinuxUpdaterFiles.ConfigureStartInfo(start);
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

    internal static class LinuxUpdaterFiles
    {
        internal static readonly string[] Libraries = ["libSkiaSharp.so", "libHarfBuzzSharp.so"];
        internal const string RuntimeManifest = "appimage-updater.files";

        public static string[] GetFiles(string programDirectory)
        {
            string[] names = ["SyncClipboard.Updater", .. Libraries];
            var manifest = Path.Combine(programDirectory, RuntimeManifest);
            if (File.Exists(manifest))
            {
                var nativeFiles = File.ReadAllLines(manifest);
                if (!nativeFiles.Contains("appimage-ld.so") || nativeFiles.Any(name => string.IsNullOrWhiteSpace(name)
                    || name != Path.GetFileName(name) || name is "." or ".."))
                    return [];
                names = [.. names, RuntimeManifest, .. nativeFiles];
            }
            var files = names.Distinct().Select(name => Path.Combine(programDirectory, name)).ToArray();
            return files.All(File.Exists) ? files : [];
        }

        public static void ConfigureStartInfo(ProcessStartInfo start)
        {
            var directory = Path.GetDirectoryName(start.FileName);
            if (directory is not null && File.Exists(Path.Combine(directory, RuntimeManifest)))
            {
                // The bundled updater's ELF interpreter is relative to this directory.
                start.WorkingDirectory = directory;
            }
            start.Environment.TryGetValue("APPDIR", out var appDir);
            if (!string.IsNullOrEmpty(appDir))
            {
                foreach (var key in new[] { "LD_LIBRARY_PATH", "PATH", "XDG_DATA_DIRS" })
                {
                    if (start.Environment.TryGetValue(key, out var value) && value is not null)
                    {
                        var paths = value.Split(':').Where(path => !Path.IsPathFullyQualified(path) || !FileSystem.IsWithin(path, appDir));
                        start.Environment[key] = string.Join(':', paths);
                    }
                }
            }
            foreach (var key in new[] { "APPDIR", "APPIMAGE", "ARGV0", "OWD" })
            {
                start.Environment.Remove(key);
            }
        }
    }
}
