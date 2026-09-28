using SyncClipboard.Core.Utilities.Updater;
using System.Diagnostics;
using System.Text.Json;

namespace SyncClipboard.Test;

[TestClass]
public class UpdateHelperTests
{
    public TestContext TestContext { get; set; } = null!;

    private string work = null!;
    private readonly List<Process> processes = [];

    [TestInitialize]
    public void Initialize() => work = Directory.CreateTempSubdirectory("SyncClipboard helper 中文 ' ").FullName;

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var process in processes)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.Dispose();
        }
        Directory.Delete(work, true);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task HelperReadiness_DistinguishesTimeoutFromCallerCancellation(bool callerCanceled)
    {
        var task = CreateTask();
        using var process = Process.GetCurrentProcess();
        using var cancellation = new CancellationTokenSource();
        if (callerCanceled) cancellation.Cancel();
        var waiting = UpdateTaskCoordinator.WaitForReadyAsync(task, process, cancellation.Token, TimeSpan.Zero);
        if (callerCanceled) await Assert.ThrowsAsync<OperationCanceledException>(() => waiting);
        else await Assert.ThrowsAsync<IOException>(() => waiting);
    }

    [TestMethod]
    public void CanceledHelper_PreventsRetryUntilItStops()
    {
        var previous = Directory.CreateDirectory(Path.Combine(work, "previous")).FullName;
        File.WriteAllText(Path.Combine(previous, "cancel"), "");
        var pid = Path.Combine(previous, "helper-pid");
        File.WriteAllText(pid, Environment.ProcessId.ToString());
        Assert.Throws<IOException>(() => UpdateTaskCoordinator.EnsurePreviousHelperStopped(work));
        File.WriteAllText(pid, int.MaxValue.ToString());
        UpdateTaskCoordinator.EnsurePreviousHelperStopped(work);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task WindowsWorker_PreservesUserFilesAndRollsBackLockedUpdates(bool lockLastFile, bool closeSupervisor)
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Requires Windows PowerShell.");
        var target = Directory.CreateDirectory(Path.Combine(work, "installation")).FullName;
        var stage = Directory.CreateDirectory(Path.Combine(work, "payload")).FullName;
        File.WriteAllText(Path.Combine(target, "SyncClipboard.exe"), "old");
        File.WriteAllText(Path.Combine(target, "user.txt"), "keep");
        File.WriteAllText(Path.Combine(stage, "SyncClipboard.exe"), "new");
        File.WriteAllText(Path.Combine(target, "Z-library.dll"), "old library");
        File.WriteAllText(Path.Combine(stage, "Z-library.dll"), "new library");
        var task = new UpdateInstallTask
        {
            Directory = work,
            Kind = "WindowsPortable",
            Target = target,
            Stage = stage,
            Backup = Path.Combine(work, "backup"),
            Executable = Path.Combine(target, "SyncClipboard.exe"),
            Version = "v9.0.0",
            ProcessId = closeSupervisor ? Environment.ProcessId : int.MaxValue,
            ProtectedPaths = [Path.Combine(target, "user.txt")]
        };
        File.WriteAllText(Path.Combine(work, "task.json"), JsonSerializer.Serialize(task));
        Directory.CreateDirectory(Path.Combine(work, "download"));
        CopyResource("InstallWindowsZip.ps1");
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(work, "InstallWindowsZip.ps1"), "-Work", work, "-Worker" }) start.ArgumentList.Add(arg);
        var supervisorStart = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 120")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var supervisor = closeSupervisor ? Process.Start(supervisorStart)! : null;
        if (supervisor is not null)
        {
            processes.Add(Process.GetProcessById(supervisor.Id));
            start.ArgumentList.Add("-SupervisorId");
            start.ArgumentList.Add(supervisor.Id.ToString());
        }
        using var process = Process.Start(start)!;
        processes.Add(Process.GetProcessById(process.Id));
        await WaitFor("ready", process);
        using var locked = lockLastFile
            ? new FileStream(Path.Combine(target, "Z-library.dll"), FileMode.Open, FileAccess.Read, FileShare.Read)
            : null;
        File.WriteAllText(Path.Combine(work, "commit"), "");
        if (supervisor is not null)
        {
            supervisor.Kill();
            await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token)
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationTokenSource.Token);
            Assert.AreEqual(1, process.ExitCode);
            Assert.IsTrue(File.Exists(Path.Combine(work, "failed")));
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(target, "SyncClipboard.exe")));
            Assert.AreEqual("old library", File.ReadAllText(Path.Combine(target, "Z-library.dll")));
            Assert.AreEqual("keep", File.ReadAllText(Path.Combine(target, "user.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(work, "installed")));
            return;
        }
        if (lockLastFile)
        {
            await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token)
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationTokenSource.Token);
            Assert.AreEqual(1, process.ExitCode);
            Assert.IsTrue(File.Exists(Path.Combine(work, "restored")));
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(target, "SyncClipboard.exe")));
            Assert.AreEqual("old library", File.ReadAllText(Path.Combine(target, "Z-library.dll")));
            Assert.AreEqual("keep", File.ReadAllText(Path.Combine(target, "user.txt")));
            return;
        }
        await WaitFor("installed", process);
        Assert.AreEqual("new", File.ReadAllText(Path.Combine(target, "SyncClipboard.exe")));
        Assert.AreEqual("keep", File.ReadAllText(Path.Combine(target, "user.txt")));
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(task.Backup, "SyncClipboard.exe")));
        await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token).WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(0, process.ExitCode);
    }

    private UpdateInstallTask CreateTask() => new()
    {
        Directory = work,
        Kind = nameof(UpdatePackageKind.WindowsPortable),
        Target = "unused",
        Stage = "unused",
        Backup = "unused",
        Executable = "unused",
        Version = "v9.0.0",
        ProcessId = int.MaxValue
    };

    private void CopyResource(string name)
    {
        using var source = typeof(UpdateInstallerDispatcher).Assembly.GetManifestResourceStream("SyncClipboard.Core.Utilities.Updater.Strategies.Scripts." + name)!;
        using var output = File.Create(Path.Combine(work, name));
        source.CopyTo(output);
    }

    private async Task WaitFor(string name, Process process)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!File.Exists(Path.Combine(work, name)))
        {
            if (process.HasExited)
            {
                Assert.Fail("Worker exited before " + name + ": " + File.ReadAllText(Path.Combine(work, "install.log")));
            }
            await Task.Delay(50, timeout.Token);
        }
    }
}
