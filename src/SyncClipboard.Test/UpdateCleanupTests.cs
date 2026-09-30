using SyncClipboard.Core.Utilities.Updater;
using System.Diagnostics;
using System.Text.Json;

namespace SyncClipboard.Test;

[TestClass]
public class UpdateCleanupTests
{
    private string root = null!;

    [TestInitialize]
    public void Initialize() => root = Directory.CreateTempSubdirectory("Updater cleanup 中文 ").FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    [TestMethod]
    [DataRow("v3.3.0", "3.4.0", true)]
    [DataRow("v3.4.0", "3.4.0", true)]
    [DataRow("v3.5.0", "3.4.0", false)]
    [DataRow("v3.4.0", "3.3.0", false)]
    [DataRow("v3.4.0-beta1", "3.4.0", true)]
    public async Task Cleanup_UsesTargetVersion(string targetVersion, string currentVersion, bool removed)
    {
        var work = CreateTask(targetVersion);
        await new UpdateTaskCleaner(root, currentVersion).CleanupCompletedAsync();
        Assert.AreEqual(!removed, Directory.Exists(work));
    }

    [TestMethod]
    [DataRow("failed")]
    [DataRow("restored")]
    [DataRow("canceled")]
    [DataRow("incomplete")]
    public async Task Cleanup_PreservesFailedAndIncompleteTasks(string state)
    {
        var work = CreateTask("v3.3.0");
        if (state == "incomplete") File.Delete(Path.Combine(work, "completed"));
        else File.WriteAllText(Path.Combine(work, state), "reason");
        await new UpdateTaskCleaner(root, "3.4.0").CleanupCompletedAsync();
        Assert.AreEqual("log", File.ReadAllText(Path.Combine(work, "install.log")));
        Assert.AreEqual("backup", File.ReadAllText(Path.Combine(work, "backup")));
    }

    [TestMethod]
    public async Task Cleanup_WaitsForHelperExitWithoutBlockingCaller()
    {
        var work = CreateTask("v3.4.0");
        using var process = StartHelper();
        File.WriteAllText(Path.Combine(work, "helper-pid"), process.Id.ToString());
        var cleanup = new UpdateTaskCleaner(root, "3.4.0").CleanupCompletedAsync();
        Assert.IsFalse(cleanup.IsCompleted);
        Assert.IsTrue(Directory.Exists(work));
        await cleanup;
        Assert.IsTrue(process.HasExited);
        Assert.IsFalse(Directory.Exists(work));
    }

    [TestMethod]
    public async Task Cleanup_RechecksFailureAfterHelperExit()
    {
        var work = CreateTask("v3.4.0");
        using var process = StartHelper();
        File.WriteAllText(Path.Combine(work, "helper-pid"), process.Id.ToString());
        var cleanup = new UpdateTaskCleaner(root, "3.4.0").CleanupCompletedAsync();
        File.WriteAllText(Path.Combine(work, "failed"), "Could not restart");
        await cleanup;
        Assert.IsTrue(Directory.Exists(work));
    }

    private static Process StartHelper()
    {
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 1")
            : new ProcessStartInfo("/bin/sleep", "1");
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        return Process.Start(start)!;
    }

    private string CreateTask(string version)
    {
        var work = Directory.CreateDirectory(Path.Combine(root, Guid.NewGuid().ToString("N"))).FullName;
        var task = new UpdateInstallTask
        {
            Directory = work,
            Kind = "FileReplacement",
            Target = "unused",
            Stage = "unused",
            Backup = "unused",
            Executable = "unused",
            Version = version,
            ProcessId = int.MaxValue
        };
        File.WriteAllText(Path.Combine(work, "task.json"), JsonSerializer.Serialize(task));
        File.WriteAllText(Path.Combine(work, "completed"), "");
        File.WriteAllText(Path.Combine(work, "helper-pid"), int.MaxValue.ToString());
        File.WriteAllText(Path.Combine(work, "install.log"), "log");
        File.WriteAllText(Path.Combine(work, "backup"), "backup");
        return work;
    }
}
