using SyncClipboard.Core.Utilities.Updater;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
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
        var waiting = FileReplacementUpdater.WaitForReadyAsync(task, process, cancellation.Token, TimeSpan.Zero);
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
        Assert.Throws<IOException>(() => FileReplacementUpdater.EnsurePreviousHelperStopped(work));
        File.WriteAllText(pid, int.MaxValue.ToString());
        FileReplacementUpdater.EnsurePreviousHelperStopped(work);
    }

    [TestMethod]
    [DataRow(false, false, null)]
    [DataRow(true, false, null)]
    [DataRow(false, true, null)]
    [DataRow(false, false, "progress")]
    [DataRow(true, false, "progress")]
    [DataRow(false, false, "progress.tmp")]
    [DataRow(true, false, "progress.tmp")]
    public async Task WindowsWorker_PreservesUserFilesAndRollsBackLockedUpdates(
        bool lockLastFile, bool closeSupervisor, string? lockedProgressFile)
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
        using var progressLock = lockedProgressFile is null ? null
            : new FileStream(Path.Combine(work, lockedProgressFile), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
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
            Assert.IsFalse(File.Exists(Path.Combine(work, "needs-elevation")));
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

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WindowsWorker_RequestsElevationAfterAclFailureOnlyOnce(bool alreadyRetried)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Requires Windows file ACLs and PowerShell.");
            return;
        }
        var target = Directory.CreateDirectory(Path.Combine(work, "installation")).FullName;
        var stage = Directory.CreateDirectory(Path.Combine(work, "payload")).FullName;
        foreach (var name in new[] { "SyncClipboard.exe", "Z-library.dll" })
        {
            File.WriteAllText(Path.Combine(target, name), "old " + name);
            File.WriteAllText(Path.Combine(stage, name), "new " + name);
        }
        var task = new UpdateInstallTask
        {
            Directory = work,
            Kind = "WindowsPortable",
            Target = target,
            Stage = stage,
            Backup = Path.Combine(work, "backup"),
            Executable = Path.Combine(target, "SyncClipboard.exe"),
            Version = "v9.0.0",
            ProcessId = int.MaxValue
        };
        File.WriteAllText(Path.Combine(work, "task.json"), JsonSerializer.Serialize(task));
        File.WriteAllText(Path.Combine(work, "commit"), "");
        CopyResource("InstallWindowsZip.ps1");
        var restricted = new FileInfo(Path.Combine(target, "Z-library.dll"));
        var originalAcl = restricted.GetAccessControl();
        var originalDescriptor = originalAcl.GetSecurityDescriptorBinaryForm();
        var deniedAcl = restricted.GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        deniedAcl.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.WriteData, AccessControlType.Deny));
        restricted.SetAccessControl(deniedAcl);
        try
        {
            Assert.AreEqual(1, await RunWindowsWorker(alreadyRetried));
            Assert.IsTrue(File.Exists(Path.Combine(work, "restored")));
            Assert.AreEqual(!alreadyRetried, File.Exists(Path.Combine(work, "needs-elevation")));
            Assert.AreEqual(alreadyRetried, File.Exists(Path.Combine(work, "failed")));
            Assert.AreEqual("old SyncClipboard.exe", File.ReadAllText(task.Executable));
            Assert.AreEqual("old Z-library.dll", File.ReadAllText(restricted.FullName));
            if (alreadyRetried) return;

            // Simulate the granted permission, then run the retry worker without an interactive UAC prompt.
            // FileSecurity read from disk has no dirty access section; explicitly mark the saved DACL for persistence.
            originalAcl.SetSecurityDescriptorBinaryForm(originalDescriptor, AccessControlSections.Access);
            restricted.SetAccessControl(originalAcl);
            File.Delete(Path.Combine(work, "needs-elevation"));
            File.Delete(Path.Combine(work, "restored"));
            Assert.AreEqual(0, await RunWindowsWorker(true));
            Assert.IsTrue(File.Exists(Path.Combine(work, "completed")));
            Assert.IsFalse(File.Exists(Path.Combine(work, "failed")));
            Assert.AreEqual("new SyncClipboard.exe", File.ReadAllText(task.Executable));
            Assert.AreEqual("new Z-library.dll", File.ReadAllText(restricted.FullName));
            Assert.AreEqual("old SyncClipboard.exe", File.ReadAllText(Path.Combine(task.Backup, "SyncClipboard.exe")));
        }
        finally
        {
            originalAcl.SetSecurityDescriptorBinaryForm(originalDescriptor, AccessControlSections.Access);
            restricted.SetAccessControl(originalAcl);
        }
    }

    [TestMethod]
    public async Task WindowsSupervisor_RestartsRestoredApplicationWhenRetryAuthorizationIsCanceled()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Requires Windows PowerShell.");
        File.WriteAllText(Path.Combine(work, "task.json"), JsonSerializer.Serialize(new
        {
            Executable = Path.Combine(work, "SyncClipboard.exe"),
            Elevate = false,
            Language = "en"
        }));
        CopyResource("InstallWindowsZip.ps1");
        var harness = Path.Combine(work, "cancel-authorization.ps1");
        File.WriteAllText(harness, """
            param([string]$Work)
            $script:workerStarts = 0
            function Start-Process {
                param($FilePath, $ArgumentList, [switch]$PassThru, $WindowStyle, $Verb, $WorkingDirectory)
                if ($FilePath -eq (Join-Path $Work 'SyncClipboard.exe')) {
                    if ($Verb) { throw 'The application must restart without elevation.' }
                    [IO.File]::WriteAllText((Join-Path $Work 'restarted-old-app'), '')
                    return
                }
                $script:workerStarts++
                if ($script:workerStarts -eq 1) {
                    [IO.File]::WriteAllText((Join-Path $Work 'restored'), '')
                    [IO.File]::WriteAllText((Join-Path $Work 'needs-elevation'), '')
                    return [pscustomobject]@{ HasExited = $true }
                }
                if ($script:workerStarts -ne 2 -or $Verb -ne 'RunAs' -or $ArgumentList -notlike '*-ElevatedRetry') {
                    throw 'Unexpected elevation retry.'
                }
                throw [ComponentModel.Win32Exception]::new(1223)
            }
            function Read-Host { return '' }
            & (Join-Path $Work 'InstallWindowsZip.ps1') -Work $Work
            exit $LASTEXITCODE
            """);
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            harness, "-Work", work }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        processes.Add(Process.GetProcessById(process.Id));
        await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token)
            .WaitAsync(TimeSpan.FromSeconds(20), TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(1, process.ExitCode);
        Assert.IsTrue(File.Exists(Path.Combine(work, "canceled")));
        Assert.IsTrue(File.Exists(Path.Combine(work, "failed")));
        Assert.IsTrue(File.Exists(Path.Combine(work, "restored")));
        Assert.IsTrue(File.Exists(Path.Combine(work, "restarted-old-app")));
        Assert.IsFalse(File.Exists(Path.Combine(work, "completed")));
    }

    [TestMethod]
    public async Task WindowsSupervisor_RestartsApplicationWhenProgressCannotBeRead()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Requires Windows PowerShell.");
        File.WriteAllText(Path.Combine(work, "task.json"), JsonSerializer.Serialize(new
        {
            Executable = Path.Combine(work, "SyncClipboard.exe"),
            Elevate = false,
            Language = "en"
        }));
        CopyResource("InstallWindowsZip.ps1");
        using var progressLock = new FileStream(Path.Combine(work, "progress"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var harness = Path.Combine(work, "locked-progress.ps1");
        File.WriteAllText(harness, """
            param([string]$Work)
            function Start-Process {
                param($FilePath, $ArgumentList, [switch]$PassThru, $WindowStyle, $Verb, $WorkingDirectory)
                if ($FilePath -eq (Join-Path $Work 'SyncClipboard.exe')) {
                    [IO.File]::WriteAllText((Join-Path $Work 'restarted-app'), '')
                    return
                }
                [IO.File]::WriteAllText((Join-Path $Work 'installed'), '')
                $worker = [pscustomobject]@{ HasExited = $false }
                $worker | Add-Member -MemberType ScriptMethod -Name Refresh -Value { $this.HasExited = $true }
                return $worker
            }
            function Read-Host { return '' }
            & (Join-Path $Work 'InstallWindowsZip.ps1') -Work $Work
            exit $LASTEXITCODE
            """);
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            harness, "-Work", work }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        processes.Add(Process.GetProcessById(process.Id));
        await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token)
            .WaitAsync(TimeSpan.FromSeconds(60), TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(0, process.ExitCode);
        Assert.IsTrue(File.Exists(Path.Combine(work, "restarted-app")));
        Assert.IsFalse(File.Exists(Path.Combine(work, "failed")));
    }

    [TestMethod]
    public async Task WindowsWorker_SkipsUnmodifiedFileLockedAfterBackupDuringRollback()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Requires Windows file sharing and PowerShell.");
        var target = Directory.CreateDirectory(Path.Combine(work, "installation")).FullName;
        var stage = Directory.CreateDirectory(Path.Combine(work, "payload")).FullName;
        foreach (var name in new[] { "SyncClipboard.exe", "Z-library.dll" })
        {
            File.WriteAllText(Path.Combine(target, name), "old " + name);
            File.WriteAllText(Path.Combine(stage, name), "new " + name);
        }
        var task = new UpdateInstallTask
        {
            Directory = work,
            Kind = "WindowsPortable",
            Target = target,
            Stage = stage,
            Backup = Path.Combine(work, "backup"),
            Executable = Path.Combine(target, "SyncClipboard.exe"),
            Version = "v9.0.0",
            ProcessId = int.MaxValue
        };
        File.WriteAllText(Path.Combine(work, "task.json"), JsonSerializer.Serialize(task));
        File.WriteAllText(Path.Combine(work, "commit"), "");
        CopyResource("InstallWindowsZip.ps1");
        var scriptPath = Path.Combine(work, "InstallWindowsZip.ps1");
        var script = File.ReadAllText(scriptPath);
        const string checkpoint = "        $replacementStarted = $true";
        Assert.IsTrue(script.Contains(checkpoint, StringComparison.Ordinal));
        // Pause the real worker after backup to acquire the exclusive lock without a timing race.
        script = script.Replace(checkpoint, """
                    Mark 'backup-ready'
                    while (!(Exists 'continue-replacement')) { Start-Sleep -Milliseconds 50 }
                    $replacementStarted = $true
            """, StringComparison.Ordinal);
        File.WriteAllText(scriptPath, script, new System.Text.UTF8Encoding(true));
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            scriptPath, "-Work", work, "-Worker" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        processes.Add(Process.GetProcessById(process.Id));
        await WaitFor("backup-ready", process);
        using (var locked = new FileStream(Path.Combine(target, "Z-library.dll"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            File.WriteAllText(Path.Combine(work, "continue-replacement"), "");
            await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token)
                .WaitAsync(TimeSpan.FromSeconds(20), TestContext.CancellationTokenSource.Token);
            Assert.AreEqual(1, process.ExitCode);
            Assert.IsTrue(File.Exists(Path.Combine(work, "restored")));
        }
        Assert.AreEqual("old SyncClipboard.exe", File.ReadAllText(task.Executable));
        Assert.AreEqual("old Z-library.dll", File.ReadAllText(Path.Combine(target, "Z-library.dll")));
        Assert.IsTrue(File.Exists(Path.Combine(work, "failed")));
        Assert.IsFalse(File.Exists(Path.Combine(work, "needs-elevation")));
        using var journal = JsonDocument.Parse(File.ReadAllText(Path.Combine(work, "journal.json")));
        var entries = journal.RootElement.EnumerateArray().ToDictionary(entry =>
            Path.GetFileName(entry.GetProperty("Destination").GetString())!, entry => entry.GetProperty("Modified").GetBoolean());
        Assert.IsTrue(entries["SyncClipboard.exe"]);
        Assert.IsFalse(entries["Z-library.dll"]);
    }

    private async Task<int> RunWindowsWorker(bool elevatedRetry)
    {
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(work, "InstallWindowsZip.ps1"), "-Work", work, "-Worker" }) start.ArgumentList.Add(argument);
        if (elevatedRetry) start.ArgumentList.Add("-ElevatedRetry");
        using var process = Process.Start(start)!;
        processes.Add(Process.GetProcessById(process.Id));
        await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token)
            .WaitAsync(TimeSpan.FromSeconds(20), TestContext.CancellationTokenSource.Token);
        var failure = Path.Combine(work, "failed");
        if (process.ExitCode != 0 && File.Exists(failure)) TestContext.WriteLine(File.ReadAllText(failure));
        return process.ExitCode;
    }

    private void CopyResource(string name)
    {
        using var source = typeof(UpdateInstallerFactory).Assembly.GetManifestResourceStream("SyncClipboard.Core.Utilities.Updater.Strategies.Scripts." + name)!;
        using var output = File.Create(Path.Combine(work, name));
        source.CopyTo(output);
    }

    private async Task WaitFor(string name, Process process)
    {
        var elapsed = Stopwatch.StartNew();
        while (!File.Exists(Path.Combine(work, name)))
        {
            if (process.HasExited)
            {
                var log = Path.Combine(work, "install.log");
                Assert.Fail($"Worker exited before {name}, exit code {process.ExitCode}: " +
                    (File.Exists(log) ? File.ReadAllText(log) : "No installation log was created."));
            }
            if (elapsed.Elapsed > TimeSpan.FromSeconds(60))
            {
                Assert.Fail($"Timed out waiting for {name}; PID {process.Id}, " +
                    $"worker script started: {File.Exists(Path.Combine(work, "worker-pid"))}.");
            }
            await Task.Delay(50, TestContext.CancellationTokenSource.Token);
        }
    }
}
