using SyncClipboard.Core.Utilities.Updater;
using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

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
        var task = CreateUnixTask();
        using var process = Process.GetCurrentProcess();
        using var cancellation = new CancellationTokenSource();
        if (callerCanceled) cancellation.Cancel();
        var waiting = UpdateInstallHelper.WaitForReadyAsync(task, process, cancellation.Token, TimeSpan.Zero);
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
        Assert.Throws<IOException>(() => UpdateInstallHelper.EnsurePreviousHelperStopped(work));
        File.WriteAllText(pid, int.MaxValue.ToString());
        UpdateInstallHelper.EnsurePreviousHelperStopped(work);
    }

    [TestMethod]
    [DataRow("backup")]
    [DataRow("installing")]
    public async Task UnixWorker_CancellationDuringCopyPreservesOriginal(string phase)
    {
        RequireUnix();
        var task = CreateUnixTask();
        var tools = Directory.CreateDirectory(Path.Combine(work, "tools")).FullName;
        var copy = Path.Combine(tools, "cp");
        File.WriteAllText(copy, """
            #!/bin/sh
            /bin/cp "$@" || exit 1
            if [ "$(sed -n '1p' "$UPDATE_TEST_WORK/progress")" = "$UPDATE_TEST_PHASE" ]; then
                touch "$UPDATE_TEST_WORK/cancel"
                sleep 1
            fi
            """);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(copy, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var process = StartUnixWorker(tools, phase);
        await WaitFor("ready", process);
        File.WriteAllText(Path.Combine(work, "commit"), "");
        await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(1, process.ExitCode);
        Assert.AreEqual("old version", File.ReadAllText(task.Target));
        Assert.IsTrue(File.Exists(Path.Combine(work, "restored")));
        Assert.IsTrue(File.Exists(Path.Combine(work, "failed")));
        Assert.IsFalse(File.Exists(Path.Combine(work, "completed")));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task MacTerminalCommand_PreservesExitStatusAndQuotesTaskPath(int exitCode)
    {
        RequireUnix();
        var originalWork = work;
        work += " $(touch injected) & \"test\"";
        Directory.Move(originalWork, work);
        var task = CreateUnixTask(mac: true);
        CopyResource("install.command");
        File.WriteAllText(Path.Combine(work, "install.sh"),
            "printf '%s' \"$1\" > \"$1/received-path\"\nexit " + exitCode + "\n");

        var elements = XDocument.Parse(MacDmgInstaller.CreateTerminalProfile(work)).Root!.Element("dict")!.Elements().ToArray();
        var settings = Enumerable.Range(0, elements.Length / 2)
            .ToDictionary(index => elements[index * 2].Value, index => elements[(index * 2) + 1]);
        Assert.AreEqual("1", settings["shellExitAction"].Value);
        Assert.AreEqual("false", settings["RunCommandAsShell"].Name.LocalName);
        var terminalStart = UpdateInstallHelper.CreateStartInfo(task);
        CollectionAssert.AreEqual(new[] { "-a", "Terminal", Path.Combine(work, "install.terminal") }, terminalStart.ArgumentList.ToArray());

        var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, WorkingDirectory = work };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(settings["CommandString"].Value);
        using var process = Process.Start(start)!;
        processes.Add(Process.GetProcessById(process.Id));
        await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(exitCode, process.ExitCode);
        Assert.AreEqual(work, File.ReadAllText(Path.Combine(work, "received-path")));
        Assert.IsFalse(File.Exists(Path.Combine(work, "injected")));
    }

    [TestMethod]
    public async Task UnixWorker_ReplacesAndLeavesCleanupToMainApplication()
    {
        RequireUnix();
        var task = CreateUnixTask();
        using var process = StartUnixWorker();
        await WaitFor("ready", process);
        File.WriteAllText(Path.Combine(work, "commit"), "");
        await WaitFor("installed", process);
        Assert.AreEqual("new version", File.ReadAllText(task.Target));
        CollectionAssert.AreEqual(new[] { task.Target }, Directory.GetFileSystemEntries(Path.GetDirectoryName(task.Target)!));
        Assert.AreEqual("old version", File.ReadAllText(task.Backup));
        if (!OperatingSystem.IsWindows()) Assert.AreNotEqual((UnixFileMode)0, File.GetUnixFileMode(task.Target) & UnixFileMode.UserExecute);
        await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token).WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(0, process.ExitCode);
        Assert.IsTrue(File.Exists(task.Backup));
        Assert.IsTrue(File.Exists(Path.Combine(work, "completed")));
    }

    [TestMethod]
    public async Task UnixWorker_CanceledHandoffLeavesOriginalUntouched()
    {
        RequireUnix();
        var task = CreateUnixTask();
        using var process = StartUnixWorker();
        await WaitFor("ready", process);
        File.WriteAllText(Path.Combine(work, "cancel"), "");
        await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token).WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationTokenSource.Token);
        Assert.AreEqual("old version", File.ReadAllText(task.Target));
        Assert.IsFalse(File.Exists(task.Backup));
        Assert.IsTrue(File.Exists(Path.Combine(work, "failed")));
    }

    [TestMethod]
    public async Task UnixWorker_DoesNotOverwritePreexistingBackup()
    {
        RequireUnix();
        var task = CreateUnixTask();
        var candidate = task.Backup;
        File.WriteAllText(candidate, "existing file");
        using var process = StartUnixWorker();
        await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(1, process.ExitCode);
        Assert.AreEqual("existing file", File.ReadAllText(candidate));
        Assert.AreEqual("old version", File.ReadAllText(task.Target));
    }

    [TestMethod]
    public async Task UnixWorker_FailedReplacementRestoresOriginal()
    {
        RequireUnix();
        var task = CreateUnixTask();
        using var process = StartUnixWorker();
        await WaitFor("ready", process);
        // The payload disappears after readiness. Backup succeeds, copying the new file fails, then rollback restores it.
        File.Delete(task.Stage);
        File.WriteAllText(Path.Combine(work, "commit"), "");
        await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token).WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationTokenSource.Token);
        Assert.AreEqual("old version", File.ReadAllText(task.Target));
        Assert.IsTrue(File.Exists(Path.Combine(work, "restored")));
        Assert.IsTrue(File.Exists(Path.Combine(work, "failed")));
    }

    [TestMethod]
    public async Task UnixWorker_ExitTimeoutDoesNotReplaceOrKillParent()
    {
        RequireUnix();
        var task = CreateUnixTask(Environment.ProcessId);
        using var process = StartUnixWorker();
        await WaitFor("ready", process);
        File.WriteAllText(Path.Combine(work, "commit"), "");
        await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token).WaitAsync(TimeSpan.FromSeconds(75), TestContext.CancellationTokenSource.Token);
        Assert.Contains("Timed out", File.ReadAllText(Path.Combine(work, "failed")));
        Assert.AreEqual("old version", File.ReadAllText(task.Target));
        Assert.IsFalse(File.Exists(task.Backup));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MacWorker_PreservesSignatureAfterReplacementOrRemovalFailure(bool failRemoval)
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("Requires macOS code signing tools.");
            return;
        }
        var task = CreateUnixTask(mac: true);
        File.Delete(task.Target);
        File.Delete(task.Stage);
        foreach (var path in new[] { task.Target, task.Stage })
        {
            Directory.CreateDirectory(Path.Combine(path, "Contents", "MacOS"));
            Directory.CreateDirectory(Path.Combine(path, "Contents", "Resources"));
            File.Copy("/usr/bin/true", Path.Combine(path, "Contents", "MacOS", "SyncClipboard"));
            File.SetUnixFileMode(Path.Combine(path, "Contents", "MacOS", "SyncClipboard"),
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
            File.WriteAllText(Path.Combine(path, "Contents", "Info.plist"), """
                <?xml version="1.0" encoding="UTF-8"?>
                <plist version="1.0"><dict>
                <key>CFBundleIdentifier</key><string>xyz.jericx.desktop.syncclipboard.test</string>
                <key>CFBundleExecutable</key><string>SyncClipboard</string>
                <key>CFBundlePackageType</key><string>APPL</string>
                </dict></plist>
                """);
            File.WriteAllText(Path.Combine(path, "Contents", "Resources", "version"), path == task.Target ? "old" : "new");
            await RunTool("/usr/bin/codesign", "--force", "--sign", "-", "--timestamp=none", path);
        }
        File.WriteAllText(Path.Combine(work, "kind.txt"), "MacBundle");
        string? tools = null;
        if (failRemoval)
        {
            tools = Directory.CreateDirectory(Path.Combine(work, "tools")).FullName;
            var remove = Path.Combine(tools, "rm");
            File.WriteAllText(remove, """
                #!/bin/sh
                target=$(cat "$UPDATE_TEST_WORK/target.txt")
                if [ "$2" = "$target" ]; then
                    /bin/rm -f "$target/Contents/Resources/version"
                    exit 1
                fi
                exec /bin/rm "$@"
                """);
            File.SetUnixFileMode(remove, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        using var process = StartUnixWorker(tools);
        await WaitFor("ready", process);
        File.WriteAllText(Path.Combine(work, "commit"), "");
        await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(failRemoval ? 1 : 0, process.ExitCode);
        Assert.IsTrue(File.Exists(Path.Combine(work, failRemoval ? "restored" : "installed")));
        Assert.AreEqual(failRemoval ? "old" : "new", File.ReadAllText(Path.Combine(task.Target, "Contents", "Resources", "version")));
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(task.Backup, "Contents", "Resources", "version")));
        await RunTool("/usr/bin/codesign", "--verify", "--deep", "--strict", task.Target);
        Assert.IsTrue(Directory.Exists(task.Backup));
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
        var task = new PreparedUpdate
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
        CopyResource("install.ps1");
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(work, "install.ps1"), "-Work", work, "-Worker" }) start.ArgumentList.Add(arg);
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

    private PreparedUpdate CreateUnixTask(int parentPid = int.MaxValue, bool mac = false)
    {
        var installed = Directory.CreateDirectory(Path.Combine(work, "installation")).FullName;
        var target = Path.Combine(installed, mac ? "SyncClipboard 中文.app" : "SyncClipboard 中文.AppImage");
        var stage = Path.Combine(work, mac ? "payload.app" : "payload");
        File.WriteAllText(target, "old version");
        File.WriteAllText(stage, "new version");
        var task = new PreparedUpdate
        {
            Directory = work,
            Kind = mac ? "MacBundle" : "AppImage",
            Target = target,
            Stage = stage,
            Backup = Path.Combine(work, "backup"),
            Executable = target,
            Version = "v9.0.0",
            ProcessId = parentPid
        };
        foreach (var (name, value) in new Dictionary<string, string>
        {
            ["kind"] = task.Kind,
            ["target"] = target,
            ["stage"] = stage,
            ["backup"] = task.Backup,
            ["executable"] = target,
            ["pid"] = parentPid.ToString(),
            ["elevate"] = "no"
        }) File.WriteAllText(Path.Combine(work, name + ".txt"), value);
        CopyResource("install.sh");
        return task;
    }

    private void CopyResource(string name)
    {
        using var source = typeof(UpdateInstaller).Assembly.GetManifestResourceStream("SyncClipboard.Core.Utilities.Updater.Scripts." + name)!;
        using var output = File.Create(Path.Combine(work, name));
        source.CopyTo(output);
    }

    private Process StartUnixWorker(string? tools = null, string? cancelPhase = null)
    {
        var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        if (tools is not null) start.Environment["PATH"] = tools + Path.PathSeparator + start.Environment["PATH"];
        start.Environment["UPDATE_TEST_WORK"] = work;
        if (cancelPhase is not null) start.Environment["UPDATE_TEST_PHASE"] = cancelPhase;
        foreach (var arg in new[] { Path.Combine(work, "install.sh"), work, "worker" }) start.ArgumentList.Add(arg);
        var process = Process.Start(start)!;
        // A separate handle lets cleanup still stop workers if an assertion fails.
        processes.Add(Process.GetProcessById(process.Id));
        return process;
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

    private static void RequireUnix()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Requires a Unix shell.");
    }

    private async Task RunTool(string tool, params string[] args)
    {
        var start = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync(TestContext.CancellationTokenSource.Token);
        await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token).WaitAsync(TimeSpan.FromSeconds(20), TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(0, process.ExitCode, await error);
    }
}
