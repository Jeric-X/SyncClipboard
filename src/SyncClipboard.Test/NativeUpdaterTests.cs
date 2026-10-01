using SyncClipboard.Test.Updater;
using SyncClipboard.Updater;
using SyncClipboard.Updater.Zip;
using System.IO.Compression;

namespace SyncClipboard.Test;

[TestClass]
public class NativeUpdaterTests : UpdaterTestBase
{
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
    [DataRow(false)]
    [DataRow(true)]
    public async Task Replacement_TypeConflictsBackUpEntireOldPathsAndCanRollBack(bool rollback)
    {
        Directory.CreateDirectory(Path.Combine(target, "a", "empty"));
        File.WriteAllText(Path.Combine(target, "a", "old.txt"), "old directory content");
        File.WriteAllText(Path.Combine(target, "b"), "old file");
        File.WriteAllText(Path.Combine(stage, "a"), "new file");
        File.WriteAllText(Path.Combine(target, "c"), "old file replaced by an empty directory");
        Directory.CreateDirectory(Path.Combine(stage, "c"));
        Directory.CreateDirectory(Path.Combine(stage, "b", "nested"));
        File.WriteAllText(Path.Combine(stage, "b", "nested", "new.txt"), "new directory content");
        var backup = Path.Combine(directory, "backup");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationTokenSource.Token);
        var backupsReadyBeforeReplacement = false;
        var task = FileReplacement.ApplyAsync(stage, target, backup, [], (phase, percent) =>
        {
            if (phase == "backup")
            {
                backupsReadyBeforeReplacement = File.ReadAllText(Path.Combine(backup, "a", "old.txt")) == "old directory content"
                    && File.ReadAllText(Path.Combine(backup, "b")) == "old file"
                    && Directory.Exists(Path.Combine(target, "a")) && File.Exists(Path.Combine(target, "b"));
            }
            if (rollback && phase == "installing" && percent == 100)
                cancel.Cancel();
        }, cancel.Token);
        if (rollback)
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => task);
            Assert.AreEqual("old directory content", File.ReadAllText(Path.Combine(target, "a", "old.txt")));
            Assert.IsTrue(Directory.Exists(Path.Combine(target, "a", "empty")));
            Assert.AreEqual("old file", File.ReadAllText(Path.Combine(target, "b")));
            Assert.AreEqual("old file replaced by an empty directory", File.ReadAllText(Path.Combine(target, "c")));
        }
        else
        {
            await task;
            Assert.AreEqual("new file", File.ReadAllText(Path.Combine(target, "a")));
            Assert.IsTrue(Directory.Exists(Path.Combine(target, "c")));
            Assert.IsEmpty(Directory.GetFileSystemEntries(Path.Combine(target, "c")));
            Assert.AreEqual("new directory content", File.ReadAllText(Path.Combine(target, "b", "nested", "new.txt")));
        }
        Assert.IsTrue(backupsReadyBeforeReplacement);
        Assert.AreEqual("old directory content", File.ReadAllText(Path.Combine(backup, "a", "old.txt")));
        Assert.IsTrue(Directory.Exists(Path.Combine(backup, "a", "empty")));
        Assert.AreEqual("old file", File.ReadAllText(Path.Combine(backup, "b")));
    }

    [TestMethod]
    public async Task Replacement_TypeConflictDoesNotRemoveProtectedDescendants()
    {
        var protectedFile = Path.Combine(target, "data", "user.json");
        Directory.CreateDirectory(Path.GetDirectoryName(protectedFile)!);
        File.WriteAllText(protectedFile, "user config");
        File.WriteAllText(Path.Combine(stage, "data"), "new file");
        await Assert.ThrowsAsync<IOException>(() => FileReplacement.ApplyAsync(stage, target,
            Path.Combine(directory, "backup"), [protectedFile], (_, _) => { }, TestContext.CancellationTokenSource.Token));
        Assert.AreEqual("user config", File.ReadAllText(protectedFile));
    }

    [TestMethod]
    [DataRow("SyncClipboard.exe")]
    [DataRow("SyncClipboard.Desktop.Default.exe")]
    public void WindowsExecutableSelection_UsesAnExistingEntryPoint(string name)
    {
        var path = Path.Combine(stage, name);
        File.WriteAllText(path, "executable");
        Assert.AreEqual(path, WindowsZipPackage.GetExecutablePath(stage));
        var winui = Path.Combine(stage, "SyncClipboard.exe");
        File.WriteAllText(winui, "winui executable");
        Assert.AreEqual(winui, WindowsZipPackage.GetExecutablePath(stage));
    }

    [TestMethod]
    public void WindowsExecutableSelection_RejectsPackagesWithoutAnEntryPoint()
    {
        Assert.Throws<FileNotFoundException>(() => WindowsZipPackage.GetExecutablePath(stage));
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
        var rollbackAvailable = false;
        var progressStates = new List<(string Phase, bool CanRollback)>();
        await Assert.ThrowsAsync<OperationCanceledException>(() => FileReplacement.ApplyAsync(stage, target,
            Path.Combine(directory, "backup"), [], (phase, percent) =>
        {
            progressStates.Add((phase, rollbackAvailable));
            if (phase == "installing" && percent >= 66)
                cancel.Cancel();
        }, cancel.Token, setRollbackAvailable: available => rollbackAvailable = available));
        Assert.IsFalse(rollbackAvailable);
        foreach (var (phase, canRollback) in progressStates)
            Assert.AreEqual(phase == "installing", canRollback);
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
}
