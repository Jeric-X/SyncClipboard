using SyncClipboard.Updater;
using SyncClipboard.Updater.Zip;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Principal;

namespace SyncClipboard.Test.Updater.Windows;

[TestClass]
[TestCategory("PlatformWindows")]
public class WindowsUpdaterTests : UpdaterTestBase
{
    [TestMethod]
    [DataRow("SyncClipboard.exe")]
    [DataRow("SyncClipboard.Desktop.Default.exe")]
    public async Task ZipInstallation_VerifiesExtractsReplacesAndCleansWorkspace(string executableName)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Requires Windows executables.");
        var executable = Path.Combine(Environment.SystemDirectory, "whoami.exe");
        var package = CreateZip(("library.dll", "new"), ("appdata/history.db", "must not overwrite"),
            ("update_info.json", """
            {"UpdateInfo":{"manage_type":"manual","update_src":"github","package_name":"test.zip"}}
            """));
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Update))
        {
            archive.CreateEntryFromFile(executable, executableName);
        }
        File.WriteAllText(Path.Combine(target, "library.dll"), "old");
        Directory.CreateDirectory(Path.Combine(target, "appdata"));
        File.WriteAllText(Path.Combine(target, "appdata", "history.db"), "user history");
        var workspace = Directory.CreateDirectory(Path.Combine(directory, "SyncClipboard-updates", Guid.NewGuid().ToString("N"))).FullName;
        File.WriteAllText(Path.Combine(workspace, ".syncclipboard-update"), "SyncClipboard updater workspace v1");
        var update = Arguments() with { PackagePath = package, Digest = Digest(package), WorkDirectory = workspace };
        var token = TestContext.CancellationTokenSource.Token;
        var attempt = Directory.CreateDirectory(Path.Combine(workspace, "attempt")).FullName;
        var payload = await WindowsZipPackage.PrepareAsync(update, attempt, token);
        var backup = Path.Combine(workspace, "backup");
        var interaction = new RecordingUpdateInteraction();
        await FileReplacement.ApplyAsync(payload, target, backup, WindowsZipPackage.GetProtectedPaths(update),
            interaction.Report, token, interaction.AskFailureActionAsync);
        Assert.AreEqual("new", File.ReadAllText(Path.Combine(target, "library.dll")));
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(backup, "library.dll")));
        Assert.AreEqual("user history", File.ReadAllText(Path.Combine(target, "appdata", "history.db")));
        CollectionAssert.AreEqual(File.ReadAllBytes(executable), File.ReadAllBytes(Path.Combine(target, executableName)));
        Assert.AreEqual(0, await UpdateWorker.CleanupAndReportAsync(update, interaction));
        Assert.AreEqual(0, interaction.Result!.ExitCode);
        Assert.IsFalse(Directory.Exists(workspace));
    }

    [TestMethod]
    public async Task LockedDestination_RollsBackEarlierChangesWithoutTouchingLockedFile()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Windows file sharing test.");
        foreach (var name in new[] { "a.txt", "z.txt" })
        {
            File.WriteAllText(Path.Combine(target, name), "old");
            File.WriteAllText(Path.Combine(stage, name), "new");
        }
        using var locked = new FileStream(Path.Combine(target, "z.txt"), FileMode.Open, FileAccess.Read, FileShare.Read);
        await Assert.ThrowsAsync<IOException>(() => ApplyAsync((_, _) => { }, TestContext.CancellationTokenSource.Token));
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(target, "a.txt")));
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(target, "z.txt")));
    }

    [TestMethod]
    public async Task DirectoryPermissionFailure_RequestsElevationOnceThenUsesErrorMenu()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows directory permission test.");
            return;
        }
        var info = new DirectoryInfo(target);
        var original = info.GetAccessControl().GetSecurityDescriptorBinaryForm();
        var denied = info.GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        denied.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.CreateFiles, AccessControlType.Deny));
        info.SetAccessControl(denied);
        var interaction = new RecordingUpdateInteraction();
        try
        {
            Assert.IsTrue(await UpdateWorker.RequiresElevationAsync([target], false, interaction, TestContext.CancellationTokenSource.Token));
            Assert.IsNull(interaction.FailurePath);
            await Assert.ThrowsAsync<UpdateAbortedException>(() => UpdateWorker.RequiresElevationAsync([target], true, interaction,
                TestContext.CancellationTokenSource.Token));
            Assert.Contains(target, interaction.FailurePath!);
        }
        finally
        {
            var restored = new DirectorySecurity();
            restored.SetSecurityDescriptorBinaryForm(original, AccessControlSections.Access);
            info.SetAccessControl(restored);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FilePermissionFailure_PromptsAndRetriesWithoutChangingPermissions(bool readOnly)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows permissions test.");
            return;
        }
        var path = Path.Combine(target, "a.txt");
        File.WriteAllText(path, "old");
        File.WriteAllText(Path.Combine(stage, "a.txt"), "new");
        var file = new FileInfo(path);
        var original = file.GetAccessControl().GetSecurityDescriptorBinaryForm();
        void RestorePermissions()
        {
            var security = new FileSecurity();
            security.SetSecurityDescriptorBinaryForm(original, AccessControlSections.Access);
            file.SetAccessControl(security);
            File.SetAttributes(path, FileAttributes.Normal);
        }
        if (readOnly)
            File.SetAttributes(path, FileAttributes.ReadOnly);
        else
        {
            using var identity = WindowsIdentity.GetCurrent();
            var security = file.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.WriteData, AccessControlType.Deny));
            file.SetAccessControl(security);
        }
        var prompts = 0;
        try
        {
            UpdateWorker.ProbeDirectoryWriteAccess(target);
            await FileReplacement.ApplyAsync(stage, target, Path.Combine(directory, "backup"), [], (_, _) => { },
                TestContext.CancellationTokenSource.Token, (failedPath, error, canRollback, _) =>
                {
                    prompts++;
                    Assert.Contains(path, failedPath);
                    Assert.IsInstanceOfType<UnauthorizedAccessException>(error);
                    Assert.IsFalse(canRollback);
                    Assert.AreEqual("old", File.ReadAllText(path));
                    if (readOnly)
                        Assert.AreNotEqual((FileAttributes)0, File.GetAttributes(path) & FileAttributes.ReadOnly);
                    RestorePermissions();
                    return Task.FromResult(UpdateFailureAction.Retry);
                });
            Assert.AreEqual(1, prompts);
            Assert.AreEqual("new", File.ReadAllText(path));
        }
        finally
        {
            RestorePermissions();
        }
    }

    [TestMethod]
    public async Task Replacement_RestoresContentAndAttributesOnRollback()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows file metadata test.");
            return;
        }
        var path = Path.Combine(target, "a.txt");
        File.WriteAllText(path, "old");
        File.WriteAllText(Path.Combine(stage, "a.txt"), "new");
        File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.Archive);
        var expectedAttributes = File.GetAttributes(path);
        using var cancel = new CancellationTokenSource();
        var install = ApplyAsync((phase, _) =>
        {
            if (phase == "installing")
                cancel.Cancel();
        }, cancel.Token);
        await Assert.ThrowsAsync<OperationCanceledException>(() => install);
        Assert.AreEqual("old", File.ReadAllText(path));
        Assert.AreEqual(expectedAttributes, File.GetAttributes(path));
    }

    [TestMethod]
    public async Task CleanupFailure_ReportsCompletedUpdateAndRemainingDirectory()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Windows file locking test.");

        var workspace = Directory.CreateDirectory(Path.Combine(directory, "SyncClipboard-updates", Guid.NewGuid().ToString("N"))).FullName;
        File.WriteAllText(Path.Combine(workspace, ".syncclipboard-update"), "SyncClipboard updater workspace v1");
        var backup = Path.Combine(workspace, "backup.zip");
        using var locked = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var interaction = new RecordingUpdateInteraction();

        var result = await UpdateWorker.CleanupAndReportAsync(Arguments() with { WorkDirectory = workspace }, interaction);

        Assert.AreEqual(1, result);
        Assert.IsTrue(File.Exists(backup));
        Assert.IsTrue(interaction.Result!.CleanupIncomplete);
        Assert.AreEqual(workspace, interaction.Result.WorkDirectory);
        Assert.IsFalse(string.IsNullOrEmpty(interaction.Result.Error));
    }

    [TestMethod]
    public async Task SelfCleanup_WaitsForExecutableReleaseAndPreservesSpecialCharactersInPath()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Windows command process cleanup test.");

        var root = Path.Combine(directory, "cleanup & %i% ! literal (test)", "SyncClipboard-updates");
        var workspace = Directory.CreateDirectory(Path.Combine(root, Guid.NewGuid().ToString("N"))).FullName;
        var updater = Path.Combine(workspace, "SyncClipboard.Updater.exe");
        File.WriteAllText(Path.Combine(workspace, ".syncclipboard-update"), "SyncClipboard updater workspace v1");
        using var locked = new FileStream(updater, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var owner = StartWaitingProcess();
        var start = UpdateWorker.CreateSelfCleanupStartInfo(workspace);
        start.RedirectStandardError = true;
        start.Environment["SYNC_CLIPBOARD_CLEANUP_PID"] = owner.Id.ToString();
        start.Environment["SYNC_CLIPBOARD_CLEANUP_START"] = owner.StartTime.ToUniversalTime().Ticks.ToString();
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync(TestContext.CancellationTokenSource.Token);
        try
        {
            await Task.Delay(300, TestContext.CancellationTokenSource.Token);
            Assert.IsFalse(process.HasExited);
            Assert.IsTrue(File.Exists(updater));

            locked.Dispose();
            Assert.IsFalse(process.HasExited);
            owner.Kill();
            await owner.WaitForExitAsync(CancellationToken.None);
            // Allow the cleanup script's 30 one-second retries plus PowerShell startup on busy runners.
            await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token)
                .WaitAsync(TimeSpan.FromMinutes(1), TestContext.CancellationTokenSource.Token);
            Assert.AreEqual(0, process.ExitCode, await error);
            Assert.IsFalse(Directory.Exists(workspace));
            Assert.IsTrue(Directory.Exists(target));
        }
        finally
        {
            if (!owner.HasExited)
                owner.Kill();
            await owner.WaitForExitAsync(CancellationToken.None);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
}
