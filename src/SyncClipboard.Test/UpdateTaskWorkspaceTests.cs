using SyncClipboard.Core.Utilities.Updater;

namespace SyncClipboard.Test;

[TestClass]
public class UpdateTaskWorkspaceTests
{
    private string root = null!;

    [TestInitialize]
    public void Initialize() => root = Directory.CreateTempSubdirectory("Updater workspace 中文 ").FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    [TestMethod]
    [DataRow("helper-pid")]
    [DataRow("worker-pid")]
    public void CanceledTask_BlocksRetryWhileEitherProcessIsRunning(string pidFile)
    {
        var work = CreateTask();
        File.WriteAllText(Path.Combine(work, "cancel"), "");
        File.WriteAllText(Path.Combine(work, pidFile), Environment.ProcessId.ToString());

        Assert.Throws<IOException>(() => FileReplacementUpdater.EnsurePreviousHelperStopped(root));
        Assert.AreEqual("backup", File.ReadAllText(Path.Combine(work, "backup")));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("invalid")]
    [DataRow("0")]
    [DataRow("-1")]
    public void CanceledTask_BlocksRetryWithUnknownProcess(string pid)
    {
        var work = CreateTask();
        File.WriteAllText(Path.Combine(work, "cancel"), "");
        File.WriteAllText(Path.Combine(work, "helper-pid"), pid);

        Assert.Throws<IOException>(() => FileReplacementUpdater.EnsurePreviousHelperStopped(root));
        Assert.IsTrue(Directory.Exists(work));
    }

    [TestMethod]
    [DataRow("cancel")]
    [DataRow("completed")]
    public void StoppedTask_AllowsNextUpdateWithoutDeletingPreviousFiles(string state)
    {
        var work = CreateTask();
        File.WriteAllText(Path.Combine(work, state), "");
        File.WriteAllText(Path.Combine(work, "helper-pid"), int.MaxValue.ToString());
        File.WriteAllText(Path.Combine(work, "worker-pid"), int.MaxValue.ToString());

        FileReplacementUpdater.EnsurePreviousHelperStopped(root);

        Assert.IsFalse(UpdateTaskWorkspace.HelpersRunning(work));
        Assert.AreEqual("backup", File.ReadAllText(Path.Combine(work, "backup")));
        Assert.AreEqual("log", File.ReadAllText(Path.Combine(work, "install.log")));
    }

    private string CreateTask()
    {
        var work = Directory.CreateDirectory(Path.Combine(root, Guid.NewGuid().ToString("N"))).FullName;
        File.WriteAllText(Path.Combine(work, "backup"), "backup");
        File.WriteAllText(Path.Combine(work, "install.log"), "log");
        return work;
    }
}
