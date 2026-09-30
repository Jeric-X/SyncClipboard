using SyncClipboard.Core.Commons;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace SyncClipboard.Core.Utilities.Updater;

internal sealed class UpdateTaskWorkspace
{
    public string TaskDirectory { get; }

    public UpdateTaskWorkspace() : this(GetTaskDirectory(Env.ProgramPath)) { }

    internal UpdateTaskWorkspace(string directory) => TaskDirectory = directory;

    private static string GetTaskDirectory(string executable)
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(executable)))));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SyncClipboardUpdater", id);
    }

    internal static bool HelpersRunning(string work)
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
