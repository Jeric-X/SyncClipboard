using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SyncClipboard.Core.Utilities.Updater;

internal sealed class UpdateInstallCleanup
{
    private readonly string currentVersion;
    public string TaskDirectory { get; }

    public UpdateInstallCleanup(IAppConfig appConfig) : this(GetTaskDirectory(Env.ProgramPath), appConfig.AppVersion) { }

    internal UpdateInstallCleanup(string directory, string version)
    {
        TaskDirectory = directory;
        currentVersion = version;
    }

    private static string GetTaskDirectory(string executable)
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(executable)))));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SyncClipboardUpdater", id);
    }

    public Task CleanupCompletedAsync() => Task.Run(async () =>
    {
        try
        {
            if (!Directory.Exists(TaskDirectory) || new DirectoryInfo(TaskDirectory).LinkTarget is not null) return;
            foreach (var work in Directory.EnumerateDirectories(TaskDirectory))
            {
                try { await CleanupTaskAsync(work); }
                catch (Exception ex) { AppCore.TryGetCurrent()?.Logger.Write("Updater", ex.ToString()); }
            }
        }
        catch (Exception ex) { AppCore.TryGetCurrent()?.Logger.Write("Updater", ex.ToString()); }
    });

    private async Task CleanupTaskAsync(string work)
    {
        if (new DirectoryInfo(work).LinkTarget is not null || !CanClean(work, currentVersion)) return;
        // A completed task must identify its helper. Unknown/legacy tasks are retained.
        var pidFile = Path.Combine(work, "helper-pid");
        if (!File.Exists(pidFile)) return;
        var deadline = DateTime.UtcNow.AddMinutes(5);
        while (HelpersRunning(work))
        {
            if (DateTime.UtcNow >= deadline) return; // Try again at the next normal startup.
            await Task.Delay(500);
        }
        // Launch failure may have been recorded while the helper was shutting down.
        if (CanClean(work, currentVersion)) Directory.Delete(work, recursive: true);
    }

    internal static bool CanClean(string work, string currentVersion)
    {
        if (!File.Exists(Path.Combine(work, "completed")) || File.Exists(Path.Combine(work, "failed"))
            || File.Exists(Path.Combine(work, "canceled")) || File.Exists(Path.Combine(work, "restored"))) return false;
        var update = JsonSerializer.Deserialize<PreparedUpdate>(File.ReadAllText(Path.Combine(work, "task.json")));
        return update is not null && Path.GetFullPath(update.Directory) == Path.GetFullPath(work)
            && AppVersion.Parse(update.Version).CompareTo(AppVersion.Parse(currentVersion)) <= 0;
    }

    private static bool HelpersRunning(string work)
    {
        foreach (var name in new[] { "helper-pid", "worker-pid" })
        {
            var file = Path.Combine(work, name);
            if (!File.Exists(file)) continue;
            // Be conservative if a record is malformed or a PID has been reused.
            if (!int.TryParse(File.ReadAllText(file), out var pid) || pid <= 0 || IsProcessRunning(pid)) return true;
        }
        return false;
    }

    internal static bool IsProcessRunning(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }
}
