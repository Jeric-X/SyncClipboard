using SyncClipboard.Updater.Zip;
using SyncClipboard.Updater;
using SyncClipboard.Core.Utilities.Updater;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

namespace SyncClipboard.Test;

[TestClass]
public class NativeUpdaterTests
{
    public TestContext TestContext { get; set; } = null!;
    private string directory = null!;
    private string target = null!;
    private string stage = null!;
    private bool startedNativeUpdater;

    [TestInitialize]
    public void Initialize()
    {
        directory = Directory.CreateTempSubdirectory("SyncClipboard native 中文 ' ").FullName;
        target = Directory.CreateDirectory(Path.Combine(directory, "target")).FullName;
        stage = Directory.CreateDirectory(Path.Combine(directory, "stage")).FullName;
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (startedNativeUpdater)
        {
            foreach (var process in Process.GetProcessesByName("SyncClipboard.Updater").Concat(Process.GetProcessesByName("SyncClipboard")))
            {
                using (process)
                {
                    try
                    {
                        if (process.MainModule?.FileName.StartsWith(directory + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase) != true)
                            continue;
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(5000);
                    }
                    catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        TestContext.WriteLine(error.Message);
                    }
                }
            }
            foreach (var log in Directory.EnumerateFiles(directory, "install.log", SearchOption.AllDirectories))
                TestContext.WriteLine(File.ReadAllText(log));
        }
        Directory.Delete(directory, true);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Arguments_RoundTripPathsDigestAndProtection(bool appElevated)
    {
        var expected = Arguments() with
        {
            WorkDirectory = Path.Combine(directory, "work"),
            Elevated = true,
            ProcessStartTime = DateTime.UtcNow.Ticks,
            AppElevated = appElevated
        };
        var actual = UpdateArguments.Parse(expected.ToCommandLine().ToArray());
        Assert.AreEqual(expected with { ProtectedPaths = actual.ProtectedPaths }, actual);
        CollectionAssert.AreEqual(expected.ProtectedPaths, actual.ProtectedPaths);
    }

    [TestMethod]
    public void Arguments_RejectInvalidAndDuplicateOptions()
    {
        var args = Arguments().ToCommandLine().ToArray();
        Assert.Throws<ArgumentException>(() => UpdateArguments.Parse([.. args, "--digest", "sha256:bad"]));
        Assert.Throws<ArgumentException>(() => UpdateArguments.Parse([.. args, "--unknown", "value"]));
        Assert.Throws<ArgumentException>(() => UpdateArguments.Parse([.. args, "--app-elevated", "true"]));
        Assert.Throws<ArgumentException>(() => UpdateArguments.Parse((Arguments() with { Digest = "sha256:bad" }).ToCommandLine().ToArray()));
    }

    [TestMethod]
    public void Arguments_AppElevationDefaultsToFalseAndRejectsInvalidValues()
    {
        var args = Arguments().ToCommandLine().ToList();
        var index = args.IndexOf("--app-elevated");
        args.RemoveRange(index, 2);

        Assert.IsFalse(UpdateArguments.Parse([.. args]).AppElevated);
        Assert.Throws<ArgumentException>(() => UpdateArguments.Parse([.. args, "--app-elevated", "yes"]));
        Assert.Throws<ArgumentException>(() => UpdateArguments.Parse([.. args, "--app-elevated"]));
    }

    [TestMethod]
    [DataRow("../escape.txt")]
    [DataRow("..\\escape.txt")]
    [DataRow("/absolute.txt")]
    [DataRow("C:/absolute.txt")]
    [DataRow("file:stream")]
    [DataRow("folder./file")]
    [DataRow("folder /file")]
    [DataRow("NUL.txt")]
    [DataRow("COM1")]
    [DataRow("LPT¹.txt")]
    [DataRow("a//b")]
    public async Task Extraction_RejectsUnsafeWindowsPaths(string name)
    {
        var zip = CreateZip((name, "bad"));
        await Assert.ThrowsAsync<InvalidDataException>(() => WindowsZipPackage.ExtractAsync(zip, stage, target, [],
            TestContext.CancellationTokenSource.Token));
        Assert.IsFalse(File.Exists(Path.Combine(directory, "escape.txt")));
    }

    [TestMethod]
    public async Task Extraction_RejectsDuplicateNamesAndSymbolicLinks()
    {
        var zip = CreateZip(("a.txt", "first"), ("A.txt", "second"));
        await Assert.ThrowsAsync<InvalidDataException>(() => WindowsZipPackage.ExtractAsync(zip, stage, target, [],
            TestContext.CancellationTokenSource.Token));
        File.Delete(zip);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("link");
            entry.ExternalAttributes = 0xA000 << 16;
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => WindowsZipPackage.ExtractAsync(zip, stage, target, [],
            TestContext.CancellationTokenSource.Token));
    }

    [TestMethod]
    public async Task Extraction_ProtectsPortableAndCustomUserData()
    {
        var zip = CreateZip(("appdata/history.db", "bad"), ("StaticConfig.json", "bad"), ("SyncClipboard.json", "bad"),
            ("custom/data.txt", "bad"), ("library.dll", "new"));
        await WindowsZipPackage.ExtractAsync(zip, stage, target, WindowsZipPackage.GetProtectedPaths(Arguments()),
            TestContext.CancellationTokenSource.Token);
        Assert.HasCount(1, Directory.GetFiles(stage));
        Assert.IsTrue(File.Exists(Path.Combine(stage, "library.dll")));
        Assert.IsEmpty(Directory.GetDirectories(stage));
    }

    [TestMethod]
    public async Task Preparation_CopiesBeforeVerifyingAndDoesNotTouchInstallation()
    {
        var zip = CreateZip(("library.dll", "new"));
        var attempt = Directory.CreateDirectory(Path.Combine(directory, "attempt")).FullName;
        await Assert.ThrowsAsync<InvalidDataException>(() => WindowsZipPackage.PrepareAsync(Arguments() with { PackagePath = zip },
            attempt, TestContext.CancellationTokenSource.Token));
        CollectionAssert.AreEqual(File.ReadAllBytes(zip), File.ReadAllBytes(Path.Combine(attempt, "package.zip")));
        Assert.IsEmpty(Directory.GetFileSystemEntries(target));
        Assert.IsFalse(Directory.Exists(Path.Combine(attempt, "payload")));
    }

    [TestMethod]
    public async Task HashVerification_AcceptsExpectedDigestAndRejectsChangedSnapshot()
    {
        var zip = CreateZip(("library.dll", "new"));
        var hash = Digest(zip);
        await PackageFiles.VerifyHashAsync(zip, hash.ToLowerInvariant(), TestContext.CancellationTokenSource.Token);
        File.AppendAllText(zip, "changed");
        await Assert.ThrowsAsync<InvalidDataException>(() => PackageFiles.VerifyHashAsync(zip, hash,
            TestContext.CancellationTokenSource.Token));
    }

    [TestMethod]
    public void TargetValidation_RejectsProtectedRoot()
    {
        Assert.Throws<IOException>(() => WindowsZipPackage.ValidateTarget(Arguments() with { ProtectedPaths = [directory] }));
    }

    [TestMethod]
    public async Task Replacement_PreservesUnrelatedFilesAndIgnoresProgressFailures()
    {
        File.WriteAllText(Path.Combine(target, "library.dll"), "old");
        File.WriteAllText(Path.Combine(target, "user.txt"), "keep");
        File.WriteAllText(Path.Combine(stage, "library.dll"), "new");
        await ApplyAsync((_, _) => throw new IOException("progress is unavailable"), TestContext.CancellationTokenSource.Token);
        Assert.AreEqual("new", File.ReadAllText(Path.Combine(target, "library.dll")));
        Assert.AreEqual("keep", File.ReadAllText(Path.Combine(target, "user.txt")));
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(directory, "backup", "library.dll")));
        Assert.IsEmpty(Directory.GetFiles(target, ".syncclipboard-*"));
    }

    [TestMethod]
    public async Task Cancellation_RestoresOldFilesAndRemovesNewFilesAndDirectories()
    {
        File.WriteAllText(Path.Combine(target, "a.txt"), "old");
        File.WriteAllText(Path.Combine(stage, "a.txt"), "new");
        Directory.CreateDirectory(Path.Combine(stage, "b"));
        File.WriteAllText(Path.Combine(stage, "b", "new.txt"), "new");
        File.WriteAllText(Path.Combine(stage, "z.txt"), "new");
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() => ApplyAsync((phase, percent) =>
        {
            if (phase == "installing" && percent >= 66)
                cancel.Cancel();
        }, cancel.Token));
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(target, "a.txt")));
        Assert.IsFalse(Directory.Exists(Path.Combine(target, "b")));
        Assert.IsFalse(File.Exists(Path.Combine(target, "z.txt")));
    }

    [TestMethod]
    [DataRow(nameof(UpdateFailureAction.Abort))]
    [DataRow(nameof(UpdateFailureAction.Retry))]
    [DataRow(nameof(UpdateFailureAction.Rollback))]
    public async Task ReplacementFailure_HonorsUserChoice(string actionName)
    {
        var action = Enum.Parse<UpdateFailureAction>(actionName);
        foreach (var name in new[] { "a.txt", "z.txt" })
        {
            File.WriteAllText(Path.Combine(target, name), "old");
            File.WriteAllText(Path.Combine(stage, name), "new");
        }
        var backup = Path.Combine(directory, "backup");
        var prompted = 0;
        var installation = FileReplacement.ApplyAsync(stage, target, backup, [], (phase, percent) =>
        {
            if (phase == "installing" && percent == 50)
                File.Delete(Path.Combine(stage, "z.txt"));
        }, TestContext.CancellationTokenSource.Token, (path, error, canRollback, _) =>
        {
            prompted++;
            Assert.Contains(Path.Combine(target, "z.txt"), path);
            Assert.IsTrue(canRollback);
            Assert.IsInstanceOfType<IOException>(error);
            Assert.AreEqual("new", File.ReadAllText(Path.Combine(target, "a.txt")));
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(backup, "a.txt")));
            if (action == UpdateFailureAction.Retry)
                File.WriteAllText(Path.Combine(stage, "z.txt"), "new");
            return Task.FromResult(action);
        });
        if (action == UpdateFailureAction.Retry)
            await installation;
        else if (action == UpdateFailureAction.Abort)
        {
            var error = await Assert.ThrowsAsync<UpdateAbortedException>(() => installation);
            Assert.AreEqual(backup, error.BackupPath);
        }
        else
            await Assert.ThrowsAsync<IOException>(() => installation);

        Assert.AreEqual(1, prompted);
        Assert.AreEqual(action == UpdateFailureAction.Rollback ? "old" : "new", File.ReadAllText(Path.Combine(target, "a.txt")));
        Assert.AreEqual(action == UpdateFailureAction.Retry ? "new" : "old", File.ReadAllText(Path.Combine(target, "z.txt")));
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(backup, "a.txt")));
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(backup, "z.txt")));
        Assert.IsEmpty(Directory.GetFiles(target, ".syncclipboard-*"));
    }

    [TestMethod]
    public async Task BackupFailure_RetryCompletesBackupsBeforeReplacingFiles()
    {
        foreach (var name in new[] { "a.txt", "z.txt" })
        {
            File.WriteAllText(Path.Combine(target, name), "old");
            File.WriteAllText(Path.Combine(stage, name), "new");
        }
        var backup = Path.Combine(directory, "backup");
        var prompted = 0;
        await FileReplacement.ApplyAsync(stage, target, backup, [], (phase, percent) =>
        {
            if (phase == "backup" && percent == 50)
                Directory.CreateDirectory(Path.Combine(backup, "z.txt"));
        }, TestContext.CancellationTokenSource.Token, (_, _, _, _) =>
        {
            prompted++;
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(target, "a.txt")));
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(target, "z.txt")));
            Directory.Delete(Path.Combine(backup, "z.txt"));
            return Task.FromResult(UpdateFailureAction.Retry);
        });
        Assert.AreEqual(1, prompted);
        foreach (var name in new[] { "a.txt", "z.txt" })
        {
            Assert.AreEqual("new", File.ReadAllText(Path.Combine(target, name)));
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(backup, name)));
        }
        Assert.IsEmpty(Directory.GetFiles(backup, ".syncclipboard-*"));
    }

    [TestMethod]
    public async Task ReplacementFailure_RetryThenRollbackKeepsOriginalBackup()
    {
        foreach (var name in new[] { "a.txt", "z.txt" })
        {
            File.WriteAllText(Path.Combine(target, name), "old");
            File.WriteAllText(Path.Combine(stage, name), "new");
        }
        var prompted = 0;
        await Assert.ThrowsAsync<IOException>(() => FileReplacement.ApplyAsync(stage, target, Path.Combine(directory, "backup"), [],
            (phase, percent) =>
            {
                if (phase == "installing" && percent == 50)
                    File.Delete(Path.Combine(stage, "z.txt"));
            }, TestContext.CancellationTokenSource.Token, (_, _, _, _) =>
                Task.FromResult(++prompted == 1 ? UpdateFailureAction.Retry : UpdateFailureAction.Rollback)));
        Assert.AreEqual(2, prompted);
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(target, "a.txt")));
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(target, "z.txt")));
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(directory, "backup", "a.txt")));
    }

    [TestMethod]
    public async Task RequestedRollbackFailure_ReportsBackupPathAndPreservesBackup()
    {
        foreach (var name in new[] { "a.txt", "z.txt" })
        {
            File.WriteAllText(Path.Combine(target, name), "old");
            File.WriteAllText(Path.Combine(stage, name), "new");
        }
        var backup = Path.Combine(directory, "backup");
        var failure = await Assert.ThrowsAsync<UpdateRecoveryException>(() => FileReplacement.ApplyAsync(stage, target, backup, [],
            (phase, percent) =>
            {
                if (phase == "installing" && percent == 50)
                    File.Delete(Path.Combine(stage, "z.txt"));
            }, TestContext.CancellationTokenSource.Token, (_, _, _, _) =>
            {
                File.Delete(Path.Combine(target, "a.txt"));
                Directory.CreateDirectory(Path.Combine(target, "a.txt"));
                return Task.FromResult(UpdateFailureAction.Rollback);
            }));
        Assert.AreEqual(backup, failure.BackupPath);
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(backup, "a.txt")));
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(backup, "z.txt")));
    }

    [TestMethod]
    [DataRow("1", nameof(UpdateFailureAction.Abort))]
    [DataRow("2", nameof(UpdateFailureAction.Retry))]
    [DataRow("3", nameof(UpdateFailureAction.Rollback))]
    [DataRow("invalid\n2", nameof(UpdateFailureAction.Retry))]
    [DataRow("", nameof(UpdateFailureAction.Rollback))]
    [DataRow("\n", nameof(UpdateFailureAction.Retry))]
    public async Task FailurePrompt_ReadsChoiceAndRollsBackOnEndOfInput(string answer, string expectedName)
    {
        var expected = Enum.Parse<UpdateFailureAction>(expectedName);
        using var input = new StringReader(answer);
        using var output = new StringWriter();
        var interaction = new ConsoleUpdateInteraction("zh-CN", input: input, output: output);
        var action = await interaction.AskFailureActionAsync("locked.dll", new IOException("file is locked"), true,
            TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(expected, action);
        Assert.Contains("locked.dll", output.ToString());
        Assert.Contains("终止", output.ToString());
        Assert.Contains("重试", output.ToString());
        Assert.Contains("回滚", output.ToString());
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
        using var input = new StringReader("1\n");
        using var output = new StringWriter();
        var interaction = new ConsoleUpdateInteraction("en", input: input, output: output);
        try
        {
            Assert.IsTrue(await UpdateWorker.RequiresElevationAsync([target], false, interaction, TestContext.CancellationTokenSource.Token));
            Assert.AreEqual("", output.ToString());
            await Assert.ThrowsAsync<UpdateAbortedException>(() => UpdateWorker.RequiresElevationAsync([target], true, interaction,
                TestContext.CancellationTokenSource.Token));
            Assert.Contains(target, output.ToString());
        }
        finally
        {
            var restored = new DirectorySecurity();
            restored.SetSecurityDescriptorBinaryForm(original, AccessControlSections.Access);
            info.SetAccessControl(restored);
        }
    }

    [TestMethod]
    public async Task RollbackFailure_CanRetryRestorationWithoutOverwritingBackup()
    {
        var path = Path.Combine(target, "a.txt");
        File.WriteAllText(path, "old");
        File.WriteAllText(Path.Combine(stage, "a.txt"), "new");
        using var cancel = new CancellationTokenSource();
        var prompts = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() => FileReplacement.ApplyAsync(stage, target,
            Path.Combine(directory, "backup"), [], (phase, _) =>
            {
                if (phase == "installing")
                {
                    File.Delete(path);
                    Directory.CreateDirectory(path);
                    cancel.Cancel();
                }
            }, cancel.Token, (failedPath, _, canRollback, token) =>
            {
                prompts++;
                Assert.Contains(path, failedPath);
                Assert.IsFalse(canRollback);
                Assert.IsFalse(token.IsCancellationRequested);
                Directory.Delete(path);
                return Task.FromResult(UpdateFailureAction.Retry);
            }));
        Assert.AreEqual(1, prompts);
        Assert.AreEqual("old", File.ReadAllText(path));
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(directory, "backup", "a.txt")));
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
    public void InstallationLock_RejectsSameTargetAndAllowsDifferentTargetsAndReacquisition()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("Requires a supported updater platform.");
            return;
        }
        var sameTarget = OperatingSystem.IsWindows() ? target.ToUpperInvariant() + "\\" : target + "/";
        using (UpdateWorker.AcquireInstallationLock(target))
        {
            Assert.Throws<IOException>(() => UpdateWorker.AcquireInstallationLock(sameTarget));
            using var other = UpdateWorker.AcquireInstallationLock(stage);
        }
        using var reacquired = UpdateWorker.AcquireInstallationLock(target);
    }

    [TestMethod]
    public async Task FailedRollback_PreservesBackupAndReportsRecoveryRequired()
    {
        File.WriteAllText(Path.Combine(target, "a.txt"), "old");
        File.WriteAllText(Path.Combine(stage, "a.txt"), "new");
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAsync<UpdateRecoveryException>(() => ApplyAsync((phase, _) =>
        {
            if (phase != "installing")
                return;
            File.Delete(Path.Combine(target, "a.txt"));
            Directory.CreateDirectory(Path.Combine(target, "a.txt"));
            cancel.Cancel();
        }, cancel.Token));
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(directory, "backup", "a.txt")));
    }

    [TestMethod]
    public async Task Cleanup_DeletesOnlyValidatedUpdaterWorkspace()
    {
        await Assert.ThrowsAsync<IOException>(() => UpdateWorker.CleanupAsync(target));
        var root = Path.Combine(directory, "SyncClipboard-updates");
        var workspace = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, ".syncclipboard-update"), "SyncClipboard updater workspace v1");
        File.WriteAllText(Path.Combine(workspace, "backup"), "old");
        await UpdateWorker.CleanupAsync(workspace);
        Assert.IsFalse(Directory.Exists(workspace));
        Assert.IsTrue(Directory.Exists(target));
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
        using var input = new StringReader("1\n");
        using var output = new StringWriter();
        var interaction = new ConsoleUpdateInteraction("zh-CN", input: input, output: output);

        var result = await UpdateWorker.CleanupAndReportAsync(Arguments() with { WorkDirectory = workspace }, interaction);

        Assert.AreEqual(1, result);
        Assert.IsTrue(File.Exists(backup));
        Assert.Contains("更新已完成，但临时文件或旧备份未清理完毕。", output.ToString());
        Assert.Contains("残留目录: " + workspace, output.ToString());
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
        using var process = Process.Start(UpdateWorker.CreateSelfCleanupStartInfo(workspace))!;
        try
        {
            await Task.Delay(300, TestContext.CancellationTokenSource.Token);
            Assert.IsFalse(process.HasExited);
            Assert.IsTrue(File.Exists(updater));

            locked.Dispose();
            await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token)
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationTokenSource.Token);
            Assert.AreEqual(0, process.ExitCode);
            Assert.IsFalse(Directory.Exists(workspace));
            Assert.IsTrue(Directory.Exists(target));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token);
            }
        }
    }

    [TestMethod]
    public async Task ParentWait_HonorsCancellationAndDoesNotWaitForReusedPid()
    {
        using var current = Process.GetCurrentProcess();
        await UpdateWorker.WaitForProcessAsync(current.Id, current.StartTime.ToUniversalTime().Ticks + 1, CancellationToken.None);
        await Assert.ThrowsAsync<OperationCanceledException>(() => UpdateWorker.WaitForProcessAsync(current.Id, 0,
            new CancellationToken(true)));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ParentWait_RequiresConfirmationBeforeForcingExit(bool confirm)
    {
        using var process = StartWaitingProcess();
        var prompted = false;
        try
        {
            var wait = UpdateWorker.WaitForProcessAsync(process.Id, process.StartTime.ToUniversalTime().Ticks,
                TestContext.CancellationTokenSource.Token, _ =>
                {
                    prompted = true;
                    Assert.IsFalse(process.HasExited);
                    return Task.FromResult(confirm ? ForceExitAction.Yes : ForceExitAction.No);
                }, TimeSpan.FromMilliseconds(50));
            if (confirm)
            {
                await wait;
                Assert.IsTrue(process.HasExited);
            }
            else
            {
                await Assert.ThrowsAsync<UpdateProcessExitException>(() => wait);
                Assert.IsFalse(process.HasExited);
            }
            Assert.IsTrue(prompted);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
            await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token);
        }
    }

    [TestMethod]
    public async Task ParentWait_RetryWaitsAgainWithoutKillingProcess()
    {
        using var process = StartWaitingProcess();
        var prompts = 0;
        try
        {
            await UpdateWorker.WaitForProcessAsync(process.Id, process.StartTime.ToUniversalTime().Ticks,
                TestContext.CancellationTokenSource.Token, async token =>
                {
                    Assert.IsFalse(process.HasExited);
                    if (++prompts == 1)
                        return ForceExitAction.Retry;
                    process.Kill();
                    await process.WaitForExitAsync(token);
                    return ForceExitAction.Retry;
                }, TimeSpan.FromMilliseconds(50));
            Assert.AreEqual(2, prompts);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
            await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token);
        }
    }

    [TestMethod]
    public async Task ParentWait_ContinuesWithoutPromptWhenProcessExits()
    {
        using var process = StartWaitingProcess();
        try
        {
            var wait = UpdateWorker.WaitForProcessAsync(process.Id, process.StartTime.ToUniversalTime().Ticks,
                TestContext.CancellationTokenSource.Token, _ => throw new AssertFailedException("Unexpected force-exit prompt."));
            process.Kill();
            await wait;
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
            await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token);
        }
    }

    [TestMethod]
    public async Task ParentWait_CancellationDoesNotPromptOrKillProcess()
    {
        using var process = StartWaitingProcess();
        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => UpdateWorker.WaitForProcessAsync(process.Id,
                process.StartTime.ToUniversalTime().Ticks, new CancellationToken(true),
                _ => throw new AssertFailedException("Cancellation must not request a forced exit.")));
            Assert.IsFalse(process.HasExited);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
            await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token);
        }
    }

    private static Process StartWaitingProcess()
    {
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sleep")
        { UseShellExecute = false, CreateNoWindow = true };
        if (OperatingSystem.IsWindows())
        {
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add("Start-Sleep -Seconds 30");
        }
        else
            start.ArgumentList.Add("30");
        return Process.Start(start)!;
    }

    [TestMethod]
    public async Task NativeAotUpdater_StartsFromPreparedWorkspaceInstallsRestartsAndCleansWorkspace()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows NativeAOT process integration test.");
            return;
        }
        var native = Environment.GetEnvironmentVariable("SYNC_CLIPBOARD_UPDATER_TEST_EXE");
        if (native is null)
            Assert.Inconclusive("Publish the NativeAOT updater and set SYNC_CLIPBOARD_UPDATER_TEST_EXE.");
        Assert.IsTrue(File.Exists(native), "The CI-published updater is missing.");
        foreach (var name in new[] { "SyncClipboard.exe", "SyncClipboard.Updater.exe" })
        {
            File.Copy(native, Path.Combine(target, name));
            File.Copy(native, Path.Combine(stage, name));
        }
        const string packageName = "SyncClipboard_win_x64_portable.zip";
        File.WriteAllText(Path.Combine(stage, "update_info.json"), JsonSerializer.Serialize(new
        { UpdateInfo = new { manage_type = "manual", update_src = "github", package_name = packageName } }));
        File.WriteAllText(Path.Combine(target, "StaticConfig.json"), "keep config");
        File.WriteAllText(Path.Combine(stage, "StaticConfig.json"), "bad config");
        File.WriteAllText(Path.Combine(stage, "installed.txt"), "new version");
        var zip = Path.Combine(directory, packageName);
        ZipFile.CreateFromDirectory(stage, zip);
        var workspace = await FileReplacementPackageInstaller.PrepareUpdaterAsync(Path.Combine(target, "SyncClipboard.Updater.exe"),
            TestContext.CancellationTokenSource.Token);
        using var identity = WindowsIdentity.GetCurrent();
        var arguments = Arguments() with
        {
            PackagePath = zip,
            Digest = Digest(zip),
            WorkDirectory = workspace,
            AppElevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)
        };
        var start = UpdateWorker.CreateStartInfo(Path.Combine(workspace, "SyncClipboard.Updater.exe"), arguments);
        start.Environment["TEMP"] = directory;
        start.Environment["TMP"] = directory;
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.CreateNoWindow = true;
        startedNativeUpdater = true;
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(TestContext.CancellationTokenSource.Token);
        var errors = process.StandardError.ReadToEndAsync(TestContext.CancellationTokenSource.Token);
        await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(0, process.ExitCode);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationTokenSource.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        while (!File.Exists(Path.Combine(target, "installed.txt")) || Directory.Exists(workspace))
            await Task.Delay(100, timeout.Token);
        Assert.AreEqual("new version", File.ReadAllText(Path.Combine(target, "installed.txt")));
        Assert.AreEqual("keep config", File.ReadAllText(Path.Combine(target, "StaticConfig.json")));
        TestContext.WriteLine(await output);
        TestContext.WriteLine(await errors);
    }

    private Task ApplyAsync(Action<string, int> progress, CancellationToken token = default)
        => FileReplacement.ApplyAsync(stage, target, Path.Combine(directory, "backup"), [], progress, token);

    private UpdateArguments Arguments() => new(Path.Combine(directory, "package 中文.zip"), "sha256:" + new string('A', 64),
        target, int.MaxValue, "zh-CN", [Path.Combine(target, "custom")]);

    private string CreateZip(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(directory, "test.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(content);
        }
        return path;
    }

    private static string Digest(string path) => "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
