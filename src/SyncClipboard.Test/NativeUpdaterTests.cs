using SyncClipboard.Updater;
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
    public void Arguments_RoundTripPathsDigestAndProtection()
    {
        var expected = Arguments() with
        {
            WorkDirectory = Path.Combine(directory, "work"),
            Elevated = true,
            LauncherId = 1234,
            LauncherStartTime = DateTime.UtcNow.AddSeconds(-1).Ticks,
            ProcessStartTime = DateTime.UtcNow.Ticks
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
        Assert.Throws<ArgumentException>(() => UpdateArguments.Parse((Arguments() with { Digest = "sha256:bad" }).ToCommandLine().ToArray()));
        Assert.Throws<ArgumentException>(() => UpdateArguments.Parse((Arguments() with { Executable = Path.Combine(directory, "other.exe") })
            .ToCommandLine().ToArray()));
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
        await WindowsZipPackage.VerifyHashAsync(zip, hash.ToLowerInvariant(), TestContext.CancellationTokenSource.Token);
        File.AppendAllText(zip, "changed");
        await Assert.ThrowsAsync<InvalidDataException>(() => WindowsZipPackage.VerifyHashAsync(zip, hash,
            TestContext.CancellationTokenSource.Token));
    }

    [TestMethod]
    public void TargetValidation_RejectsProtectedRoot()
    {
        Assert.Throws<IOException>(() => WindowsZipPackage.ValidateTarget(Arguments() with { ProtectedPaths = [directory] }));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Replacement_AllowsLinkedInstallationPaths(bool linkInstallationRoot)
    {
        if (OperatingSystem.IsWindows())
            Assert.Inconclusive("Symbolic link creation requires a separate Windows privilege.");
        var actual = Directory.CreateDirectory(Path.Combine(directory, "actual")).FullName;
        File.WriteAllText(Path.Combine(actual, "library.dll"), "old");
        var link = Path.Combine(target, "linked");
        Directory.CreateSymbolicLink(link, actual);
        var installTarget = linkInstallationRoot ? link : target;
        var relative = linkInstallationRoot ? "library.dll" : Path.Combine("linked", "library.dll");
        var source = Path.Combine(stage, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "new");
        var backup = Path.Combine(directory, "backup");

        WindowsZipPackage.ValidateTarget(Arguments() with { Target = installTarget });
        await FileReplacement.ApplyAsync(stage, installTarget, backup, [], (_, _) => { },
            TestContext.CancellationTokenSource.Token);

        Assert.AreEqual("new", File.ReadAllText(Path.Combine(actual, "library.dll")));
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(backup, relative)));
    }

    [TestMethod]
    public async Task Replacement_OverwritesLinkedFile()
    {
        if (OperatingSystem.IsWindows())
            Assert.Inconclusive("Symbolic link creation requires a separate Windows privilege.");
        var actual = Path.Combine(directory, "old.dll");
        File.WriteAllText(actual, "old");
        var destination = Path.Combine(target, "library.dll");
        File.CreateSymbolicLink(destination, actual);
        File.WriteAllText(Path.Combine(stage, "library.dll"), "new");

        await ApplyAsync((_, _) => { }, TestContext.CancellationTokenSource.Token);

        Assert.AreEqual("new", File.ReadAllText(destination));
        Assert.IsNull(new FileInfo(destination).LinkTarget);
        Assert.AreEqual("old", File.ReadAllText(actual));
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(directory, "backup", "library.dll")));
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
        }, TestContext.CancellationTokenSource.Token, (path, error, _) =>
        {
            prompted++;
            Assert.AreEqual(Path.Combine(target, "z.txt"), path);
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
        }, TestContext.CancellationTokenSource.Token, (_, _, _) =>
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
            }, TestContext.CancellationTokenSource.Token, (_, _, _) =>
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
            }, TestContext.CancellationTokenSource.Token, (_, _, _) =>
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
        var action = await interaction.AskFailureActionAsync("locked.dll", new IOException("file is locked"),
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
    public async Task PermissionFailure_RestoresFilesBeforeRequestingElevation()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows file permissions test.");
            return;
        }
        foreach (var name in new[] { "a.txt", "z.txt" })
        {
            File.WriteAllText(Path.Combine(target, name), "old");
            File.WriteAllText(Path.Combine(stage, name), "new");
        }
        var protectedFile = new FileInfo(Path.Combine(target, "z.txt"));
        var original = protectedFile.GetAccessControl();
        var denied = protectedFile.GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        denied.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.WriteData, AccessControlType.Deny));
        protectedFile.SetAccessControl(denied);
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ApplyAsync((_, _) => { }, TestContext.CancellationTokenSource.Token));
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(target, "a.txt")));
            Assert.AreEqual("old", File.ReadAllText(protectedFile.FullName));
        }
        finally
        {
            protectedFile.SetAccessControl(original);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Replacement_PreservesWindowsAccessRulesAndRestoresAttributesOnRollback(bool rollback)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows file metadata test.");
            return;
        }
        var path = Path.Combine(target, "a.txt");
        File.WriteAllText(path, "old");
        File.WriteAllText(Path.Combine(stage, "a.txt"), "new");
        var file = new FileInfo(path);
        var security = file.GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        security.SetAccessRuleProtection(true, preserveInheritance: true);
        security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ReadPermissions, AccessControlType.Allow));
        file.SetAccessControl(security);
        File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.Archive);
        var expectedSecurity = file.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        var expectedAttributes = File.GetAttributes(path);
        using var cancel = new CancellationTokenSource();
        var install = ApplyAsync((phase, _) =>
        {
            if (rollback && phase == "installing")
                cancel.Cancel();
        }, cancel.Token);
        if (rollback)
            await Assert.ThrowsAsync<OperationCanceledException>(() => install);
        else
            await install;
        Assert.AreEqual(rollback ? "old" : "new", File.ReadAllText(path));
        if (rollback)
            Assert.AreEqual(expectedAttributes, File.GetAttributes(path));
        Assert.AreEqual(expectedSecurity, new FileInfo(path).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access));
    }

    [TestMethod]
    public void InstallationLock_RejectsSameTargetAndAllowsDifferentTargetsAndReacquisition()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows global semaphore test.");
            return;
        }
        using (UpdateWorker.AcquireInstallationLock(target))
        {
            Assert.Throws<IOException>(() => UpdateWorker.AcquireInstallationLock(target.ToUpperInvariant() + "\\"));
            using var other = UpdateWorker.AcquireInstallationLock(stage);
        }
        using var reacquired = UpdateWorker.AcquireInstallationLock(target);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReadOnlyDestination_CanBeReplacedAndRestoredOnRollback(bool rollback)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows read-only file test.");
            return;
        }
        var path = Path.Combine(target, "a.txt");
        File.WriteAllText(path, "old");
        File.WriteAllText(Path.Combine(stage, "a.txt"), "new");
        File.SetAttributes(path, FileAttributes.ReadOnly | FileAttributes.Archive);
        using var cancel = new CancellationTokenSource();
        try
        {
            var install = ApplyAsync((phase, _) =>
            {
                if (rollback && phase == "installing")
                    cancel.Cancel();
            }, cancel.Token);
            if (rollback)
                await Assert.ThrowsAsync<OperationCanceledException>(() => install);
            else
                await install;
            Assert.AreEqual(rollback ? "old" : "new", File.ReadAllText(path));
            Assert.AreEqual(rollback, (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [TestMethod]
    public async Task ReadOnlyDestination_FailedReplacementRestoresAttributes()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows read-only file test.");
            return;
        }
        var path = Path.Combine(target, "a.txt");
        var source = Path.Combine(stage, "a.txt");
        File.WriteAllText(path, "old");
        File.WriteAllText(source, "new");
        File.SetAttributes(path, FileAttributes.ReadOnly | FileAttributes.Archive);
        var attributes = File.GetAttributes(path);
        try
        {
            await Assert.ThrowsAsync<FileNotFoundException>(() => ApplyAsync((phase, _) =>
            {
                if (phase == "backup")
                    File.Delete(source);
            }, TestContext.CancellationTokenSource.Token));
            Assert.AreEqual("old", File.ReadAllText(path));
            Assert.AreEqual(attributes, File.GetAttributes(path));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
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
    [DataRow(false)]
    [DataRow(true)]
    public async Task Cleanup_DeletesOnlyValidatedUpdaterWorkspace(bool linkedRoot)
    {
        if (linkedRoot && OperatingSystem.IsWindows())
            Assert.Inconclusive("Symbolic link creation requires a separate Windows privilege.");
        await Assert.ThrowsAsync<IOException>(() => UpdateWorker.CleanupAsync(target, int.MaxValue, 0));
        var root = Path.Combine(directory, "SyncClipboard-updates");
        if (linkedRoot)
            Directory.CreateSymbolicLink(root, Directory.CreateDirectory(Path.Combine(directory, "actual-work")).FullName);
        var workspace = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, ".syncclipboard-update"), "SyncClipboard updater workspace v1");
        File.WriteAllText(Path.Combine(workspace, "backup"), "old");
        await UpdateWorker.CleanupAsync(workspace, int.MaxValue, 0);
        Assert.IsFalse(Directory.Exists(workspace));
        Assert.IsTrue(Directory.Exists(target));
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
                    return Task.FromResult(confirm);
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
    public async Task ParentWait_DeclinedPromptAllowsRestartWhenProcessExitedDuringPrompt()
    {
        using var process = StartWaitingProcess();
        try
        {
            var error = await Assert.ThrowsAsync<IOException>(() => UpdateWorker.WaitForProcessAsync(process.Id,
                process.StartTime.ToUniversalTime().Ticks, TestContext.CancellationTokenSource.Token, async token =>
                {
                    process.Kill();
                    await process.WaitForExitAsync(token);
                    return false;
                }, TimeSpan.FromMilliseconds(50)));
            Assert.IsNotInstanceOfType<UpdateProcessExitException>(error);
            Assert.IsTrue(process.HasExited);
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
    public async Task NativeAotUpdater_RelocatesInstallsRestartsAndCleansWorkspace()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Windows NativeAOT process integration test.");
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
        var arguments = Arguments() with
        {
            PackagePath = zip,
            Digest = Digest(zip)
        };
        var start = UpdateWorker.CreateStartInfo(Path.Combine(target, "SyncClipboard.Updater.exe"), arguments);
        start.Environment["TEMP"] = directory;
        start.Environment["TMP"] = directory;
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.CreateNoWindow = true;
        startedNativeUpdater = true;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.CancellationTokenSource.Token);
        var errors = process.StandardError.ReadToEndAsync(TestContext.CancellationTokenSource.Token);
        await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(0, process.ExitCode);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationTokenSource.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var workRoot = Path.Combine(directory, "SyncClipboard-updates");
        while (!File.Exists(Path.Combine(target, "installed.txt")) || Directory.GetDirectories(workRoot).Length != 0)
            await Task.Delay(100, timeout.Token);
        Assert.AreEqual("new version", File.ReadAllText(Path.Combine(target, "installed.txt")));
        Assert.AreEqual("keep config", File.ReadAllText(Path.Combine(target, "StaticConfig.json")));
        TestContext.WriteLine(await output);
        TestContext.WriteLine(await errors);
    }

    private Task ApplyAsync(Action<string, int> progress, CancellationToken token = default)
        => FileReplacement.ApplyAsync(stage, target, Path.Combine(directory, "backup"), [], progress, token);

    private UpdateArguments Arguments() => new(Path.Combine(directory, "package 中文.zip"), "sha256:" + new string('A', 64),
        target, Path.Combine(target, "SyncClipboard.exe"), int.MaxValue, "zh-CN", [Path.Combine(target, "custom")]);

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
