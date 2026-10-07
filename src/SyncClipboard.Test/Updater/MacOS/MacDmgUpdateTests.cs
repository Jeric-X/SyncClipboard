using SyncClipboard.Core.Commons;
using SyncClipboard.Updater.Dmg;
using SyncClipboard.Core.Utilities.Updater;
using SyncClipboard.Updater;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.Versioning;

namespace SyncClipboard.Test.Updater.MacOS;

[TestClass]
[TestCategory("PlatformMacOS")]
[SupportedOSPlatform("macos")]
public class MacDmgUpdateTests
{
    public TestContext TestContext { get; set; } = null!;
    private string directory = null!;
    private CancellationToken Token => TestContext.CancellationTokenSource.Token;

    [TestInitialize]
    public void Initialize()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Inconclusive("Requires macOS disk image and code signing tools.");
        directory = Directory.CreateTempSubdirectory("SyncClipboard DMG 中文 ' ").FullName;
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (directory is not null)
            Directory.Delete(directory, true);
    }

    [TestMethod]
    public async Task DmgInstallation_ReplacesBundlePreservesSignatureAndCleansBackup()
    {
        var update = await CreateUpdateAsync();
        var attempt = Directory.CreateDirectory(Path.Combine(directory, "attempt")).FullName;
        var package = await MacDmgPackage.PrepareAsync(update, attempt, Token);
        var replacement = new MacBundleReplacement(update.Target, Path.Combine(directory, "backup", "SyncClipboard.app"), false);
        var interaction = new Interaction();
        try
        {
            await replacement.ApplyAsync(package.BundlePath, interaction, Token);
        }
        finally
        {
            await package.DetachAsync(null);
        }
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(Path.Combine(attempt, "mount")));

        var resources = Path.Combine(update.Target, "Contents", "Resources");
        Assert.AreEqual("new", File.ReadAllText(Path.Combine(resources, "version")));
        Assert.IsFalse(File.Exists(Path.Combine(resources, "old-only")));
        Assert.AreEqual("version", new FileInfo(Path.Combine(resources, "current")).LinkTarget);
        await MacCommand.RunAsync("/usr/bin/codesign", ["--verify", "--deep", "--strict", update.Target], Token);
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(replacement.Backup, "Contents", "Resources", "version")));
        Assert.IsEmpty(Directory.GetDirectories(Path.GetDirectoryName(update.Target)!, ".SyncClipboard-update-*"));
        await replacement.CleanupAsync(interaction);
        Assert.IsFalse(Directory.Exists(replacement.Backup));
        Assert.IsEmpty(Directory.GetDirectories(Path.GetDirectoryName(update.Target)!, ".SyncClipboard-update-*"));
    }

    [TestMethod]
    [DataRow("Retry")]
    [DataRow("Abort")]
    [DataRow("CancelTask")]
    public async Task PreparationFailure_OffersRetryOrAbortWithOriginalBundleIntact(string choice)
    {
        var target = await CreateBundleAsync(Path.Combine(directory, "installed", "SyncClipboard.app"), "old");
        var stage = await CreateBundleAsync(Path.Combine(directory, "stage", "SyncClipboard.app"), "new");
        var replacement = new MacBundleReplacement(target, Path.Combine(directory, "backup", "SyncClipboard.app"), false);
        var unreadable = Path.Combine(stage, "Contents", "Resources", "version");
        var originalMode = File.GetUnixFileMode(unreadable);
        var prompts = 0;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var interaction = new Interaction
        {
            OnReport = phase =>
            {
                if (phase == "installing" && prompts == 0)
                    File.SetUnixFileMode(unreadable, UnixFileMode.None);
            },
            OnFailure = (_, _, canRollback, _) =>
            {
                prompts++;
                Assert.IsFalse(canRollback);
                Assert.AreEqual("old", File.ReadAllText(Path.Combine(target, "Contents", "Resources", "version")));
                Assert.HasCount(1, Directory.GetDirectories(Path.GetDirectoryName(target)!, ".SyncClipboard-update-*"));
                File.SetUnixFileMode(unreadable, originalMode);
                if (choice == "CancelTask")
                {
                    cancellation.Cancel();
                    return Task.FromCanceled<UpdateFailureAction>(cancellation.Token);
                }
                return Task.FromResult(Enum.Parse<UpdateFailureAction>(choice));
            }
        };
        if (choice == "Retry")
            await replacement.ApplyAsync(stage, interaction, cancellation.Token);
        else if (choice == "CancelTask")
            await Assert.ThrowsAsync<OperationCanceledException>(() => replacement.ApplyAsync(stage, interaction, cancellation.Token));
        else
        {
            var error = await Assert.ThrowsAsync<UpdateAbortedException>(() => replacement.ApplyAsync(stage, interaction, cancellation.Token));
            Assert.AreEqual(replacement.Backup, error.BackupPath);
        }
        Assert.AreEqual(1, prompts);
        Assert.AreEqual(choice == "Retry" ? "new" : "old",
            File.ReadAllText(Path.Combine(target, "Contents", "Resources", "version")));
        await MacCommand.RunAsync("/usr/bin/codesign", ["--verify", "--deep", "--strict", target], Token);
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(replacement.Backup, "Contents", "Resources", "version")));
        Assert.IsEmpty(Directory.GetDirectories(Path.GetDirectoryName(target)!, ".SyncClipboard-update-*"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationDuringReplacement_RestoresOriginalBundle(bool locallyModified)
    {
        var target = await CreateBundleAsync(Path.Combine(directory, "installed", "SyncClipboard.app"), "old");
        var originalVersion = locallyModified ? "locally modified" : "old";
        var versionPath = Path.Combine(target, "Contents", "Resources", "version");
        if (locallyModified)
            await File.WriteAllTextAsync(versionPath, originalVersion, Token);
        var stage = await CreateBundleAsync(Path.Combine(directory, "stage", "SyncClipboard.app"), "new");
        var replacement = new MacBundleReplacement(target, Path.Combine(directory, "backup", "SyncClipboard.app"), false);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var interaction = new Interaction
        {
            OnRollbackAvailability = available =>
            {
                if (available)
                    cancel.Cancel();
            }
        };
        await Assert.ThrowsAsync<IOException>(() => replacement.ApplyAsync(stage, interaction, cancel.Token));
        Assert.AreEqual(originalVersion, File.ReadAllText(versionPath));
        if (!locallyModified)
            await MacCommand.RunAsync("/usr/bin/codesign", ["--verify", "--deep", "--strict", target], Token);
        Assert.IsEmpty(Directory.GetDirectories(Path.GetDirectoryName(target)!, ".SyncClipboard-update-*"));
    }

    [TestMethod]
    public void BundleSwap_ExchangesBothNonemptyDirectories()
    {
        var target = Directory.CreateDirectory(Path.Combine(directory, "old.app")).FullName;
        var prepared = Directory.CreateDirectory(Path.Combine(directory, "new.app")).FullName;
        File.WriteAllText(Path.Combine(target, "version"), "old");
        File.WriteAllText(Path.Combine(prepared, "version"), "new");
        MacBundleSwap.Replace(prepared, target);
        Assert.AreEqual("new", File.ReadAllText(Path.Combine(target, "version")));
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(prepared, "version")));
    }

    [TestMethod]
    [DataRow(45, true)]
    [DataRow(102, true)]
    [DataRow(13, false)]
    [DataRow(16, false)]
    public void BundleSwap_FallsBackOnlyWhenUnsupported(int error, bool fallback)
    {
        var target = Directory.CreateDirectory(Path.Combine(directory, "old.app")).FullName;
        var prepared = Directory.CreateDirectory(Path.Combine(directory, "new.app")).FullName;
        File.WriteAllText(Path.Combine(target, "version"), "old");
        File.WriteAllText(Path.Combine(prepared, "version"), "new");
        if (fallback)
        {
            MacBundleSwap.HandleSwapFailure(prepared, target, error);
            Assert.AreEqual("new", File.ReadAllText(Path.Combine(target, "version")));
            Assert.IsFalse(Directory.Exists(prepared));
        }
        else
        {
            Assert.Throws<IOException>(() => MacBundleSwap.HandleSwapFailure(prepared, target, error));
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(target, "version")));
            Assert.AreEqual("new", File.ReadAllText(Path.Combine(prepared, "version")));
        }
    }

    [TestMethod]
    public async Task DirectoryPreflight_RequestsElevationForReadOnlyBundleInWritableParent()
    {
        var bundle = Directory.CreateDirectory(Path.Combine(directory, "SyncClipboard.app")).FullName;
        var mode = File.GetUnixFileMode(bundle);
        File.SetUnixFileMode(bundle, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            Assert.IsTrue(await UpdateWorker.RequiresElevationAsync([directory, bundle], false, new Interaction(), Token));
        }
        finally
        {
            File.SetUnixFileMode(bundle, mode);
        }
    }

    [TestMethod]
    public async Task CleanupFailure_ReportsRemainingWorkspaceAfterBackupWasRemoved()
    {
        var workspace = Directory.CreateDirectory(Path.Combine(directory, "SyncClipboard-updates", Guid.NewGuid().ToString("N"))).FullName;
        File.WriteAllText(Path.Combine(workspace, ".syncclipboard-update"), "SyncClipboard updater workspace v1");
        var backup = Directory.CreateDirectory(Path.Combine(workspace, "backup", "SyncClipboard.app")).FullName;
        File.WriteAllText(Path.Combine(backup, "old"), "old");
        var remaining = Directory.CreateDirectory(Path.Combine(workspace, "remaining")).FullName;
        File.WriteAllText(Path.Combine(remaining, "file"), "remaining");
        var mode = File.GetUnixFileMode(remaining);
        File.SetUnixFileMode(remaining, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var interaction = new Interaction { OnFailure = (_, _, _, _) => Task.FromResult(UpdateFailureAction.Abort) };
        var update = new UpdateArguments("update.dmg", "sha256:" + new string('A', 64), "SyncClipboard.app",
            int.MaxValue, "en", [], WorkDirectory: workspace);
        try
        {
            Assert.AreEqual(1, await UpdateWorker.CleanupAndReportAsync(update, interaction,
                new MacBundleReplacement(update.Target, backup, false)));
            Assert.IsNotNull(interaction.Result);
            Assert.IsTrue(interaction.Result.CleanupIncomplete);
            Assert.AreEqual(workspace, interaction.Result.WorkDirectory);
            Assert.IsNull(interaction.Result.BackupPath);
            Assert.IsTrue(File.Exists(Path.Combine(remaining, "file")));
        }
        finally
        {
            File.SetUnixFileMode(remaining, mode);
        }
    }

    [TestMethod]
    public async Task MainApplication_PreparesExecutableAndSharedLibrariesWithoutChangingBundle()
    {
        var bundle = await CreateBundleAsync(Path.Combine(directory, "SyncClipboard.app"), "old");
        var program = Path.Combine(bundle, "Contents", "MonoBundle");
        foreach (var name in MacUpdaterFiles.Libraries)
            await File.WriteAllTextAsync(Path.Combine(program, name), name, Token);
        var helper = Path.Combine(bundle, "Contents", "Resources", "Updater", "SyncClipboard.Updater");
        Directory.CreateDirectory(Path.GetDirectoryName(helper)!);
        File.Copy("/usr/bin/true", helper);
        await MacCommand.RunAsync("/usr/bin/codesign", ["--force", "--deep", "--sign", "-", bundle], Token);
        var workspace = await FileReplacementPackageInstaller.PrepareMacUpdaterAsync(bundle, Token);
        try
        {
            foreach (var name in MacUpdaterFiles.Libraries)
                Assert.AreEqual(name, File.ReadAllText(Path.Combine(workspace, name)));
            using var copiedHelper = Process.Start(new ProcessStartInfo(Path.Combine(workspace, "SyncClipboard.Updater"))
            {
                UseShellExecute = false
            })!;
            await copiedHelper.WaitForExitAsync(Token);
            Assert.AreEqual(0, copiedHelper.ExitCode);
            await MacCommand.RunAsync("/usr/bin/codesign", ["--verify", "--deep", "--strict", bundle], Token);
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task DmgInstallation_RestartsUpdatedOrRolledBackApplication(bool rollback)
    {
        var update = await CreateUpdateAsync();
        var workspace = Directory.CreateDirectory(Path.Combine(directory, "SyncClipboard-updates", Guid.NewGuid().ToString("N"))).FullName;
        File.WriteAllText(Path.Combine(workspace, ".syncclipboard-update"), "SyncClipboard updater workspace v1");
        update = update with { WorkDirectory = workspace };
        var attempt = Directory.CreateDirectory(Path.Combine(workspace, "attempt")).FullName;
        var package = await MacDmgPackage.PrepareAsync(update, attempt, Token);
        var replacement = new MacBundleReplacement(update.Target, Path.Combine(workspace, "backup", "SyncClipboard.app"), false);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var interaction = new Interaction
        {
            OnRollbackAvailability = available =>
            {
                if (rollback && available)
                    cancellation.Cancel();
            }
        };
        var detached = false;
        try
        {
            if (rollback)
                await Assert.ThrowsAsync<IOException>(() => replacement.ApplyAsync(package.BundlePath, interaction, cancellation.Token));
            else
                await replacement.ApplyAsync(package.BundlePath, interaction, cancellation.Token);
            await UpdateWorker.RestartAsync(update, updateCompleted: !rollback);
            Assert.AreEqual(0, await UpdateWorker.CleanupAndReportAsync(update, interaction, replacement, package));
            detached = true;
            Assert.AreEqual(0, interaction.Result!.ExitCode);
            Assert.IsFalse(Directory.Exists(workspace));
            Assert.IsFalse(Directory.Exists(replacement.Backup));
            Assert.IsEmpty(Directory.GetDirectories(Path.GetDirectoryName(update.Target)!, ".SyncClipboard-update-*"));
            await MacCommand.RunAsync("/usr/bin/codesign", ["--verify", "--deep", "--strict", update.Target], Token);
            var marker = Path.Combine(directory, "restarted");
            using var launchTimeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
            launchTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (!File.Exists(marker))
            {
                await Task.Delay(100, launchTimeout.Token);
            }
            string[] expected = rollback ? ["old"] : ["new", StartArguments.UpdateCompleted];
            CollectionAssert.AreEqual(expected, File.ReadAllLines(marker));
        }
        finally
        {
            if (!detached)
                await package.DetachAsync(null);
        }
    }

    [TestMethod]
    public async Task PackagePreparationRetry_RemovesFailedAttemptBeforeRetrying()
    {
        var update = await CreateUpdateAsync();
        var original = update.PackagePath + ".unavailable";
        File.Move(update.PackagePath, original);
        string? failedAttempt = null;
        var interaction = new Interaction
        {
            OnFailure = (_, _, _, _) =>
            {
                if (failedAttempt is not null)
                    throw new AssertFailedException("Preparation should succeed after restoring the package.");
                failedAttempt = Directory.GetDirectories(directory, "attempt-*").Single();
                File.Move(original, update.PackagePath);
                return Task.FromResult(UpdateFailureAction.Retry);
            }
        };
        var (Attempt, Stage, Dmg) = await UpdateWorker.PreparePackageAsync(update, interaction, Token);
        try
        {
            Assert.IsNotNull(failedAttempt);
            Assert.IsFalse(Directory.Exists(failedAttempt));
            Assert.HasCount(1, Directory.GetDirectories(directory, "attempt-*"));
            Assert.IsNotNull(Dmg);
        }
        finally
        {
            if (Dmg is not null)
                await Dmg.DetachAsync(null);
        }
    }

    [TestMethod]
    public async Task FailedAttemptCleanup_DetachesItsMountedImageBeforeDeletingFiles()
    {
        var update = await CreateUpdateAsync();
        var attempt = Directory.CreateDirectory(Path.Combine(directory, "attempt-mounted")).FullName;
        var package = await MacDmgPackage.PrepareAsync(update, attempt, Token);
        var detached = false;
        try
        {
            Assert.IsTrue(Directory.Exists(package.BundlePath));
            await MacDmgPackage.DetachAttemptAsync(attempt);
            detached = true;
            Directory.Delete(attempt, true);
            Assert.IsFalse(Directory.Exists(attempt));
        }
        finally
        {
            if (!detached)
                await package.DetachAsync(null);
        }
    }

    [TestMethod]
    public async Task CanceledCommand_StopsBeforeReturningCancellation()
    {
        var marker = Path.Combine(directory, "command-started");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var operation = MacCommand.RunAsync("/bin/sh", ["-c", "echo ready > \"$1\"; exec /bin/sleep 60", "sh", marker], cancel.Token);
        try
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(Token);
            wait.CancelAfter(TimeSpan.FromSeconds(10));
            while (!File.Exists(marker))
                await Task.Delay(20, wait.Token);
            await cancel.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(35), Token));
        }
        finally
        {
            await cancel.CancelAsync();
        }
    }

    private async Task<UpdateArguments> CreateUpdateAsync()
    {
        var source = Path.Combine(directory, "image");
        await CreateBundleAsync(Path.Combine(source, "SyncClipboard.app"), "new");
        var target = await CreateBundleAsync(Path.Combine(directory, "installed", "SyncClipboard.app"), "old");
        var createdImage = Path.Combine(directory, "created.dmg");
        var package = Path.Combine(directory, "update.dmg");
        await MacCommand.RunAsync("/usr/bin/hdiutil", ["create", "-srcfolder", source, "-format", "UDZO", createdImage], Token);
        // Native cp can read through advisory locks left by diskimages-helper; the copy has its own inode.
        await MacCommand.RunAsync("/bin/cp", [createdImage, package], Token);
        await LogCommandAsync("/usr/sbin/lsof", ["-nP", "+c", "0", "--", createdImage, package]);
        await LogCommandAsync("/usr/bin/hdiutil", ["info"]);
        var digest = await ReadPackageDigestAsync(package);
        return new UpdateArguments(package, digest, target,
            int.MaxValue, "en", [], directory);
    }

    private async Task LogCommandAsync(string executable, string[] arguments)
    {
        TestContext.WriteLine("DMG diagnostic: " + executable + " " + string.Join(" ", arguments));
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            TestContext.WriteLine(await MacCommand.RunAsync(executable, arguments, timeout.Token));
        }
        catch (Exception diagnosticError)
        {
            TestContext.WriteLine("DMG diagnostic failed: " + diagnosticError.Message);
        }
    }

    private async Task<string> ReadPackageDigestAsync(string package)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(package, Token);
                return "sha256:" + Convert.ToHexString(SHA256.HashData(bytes));
            }
            catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(30))
            {
                // Retry transient I/O failures while reading the test package.
                await Task.Delay(200, Token);
            }
        }
    }

    private async Task<string> CreateBundleAsync(string bundle, string version)
    {
        var contents = Path.Combine(bundle, "Contents");
        Directory.CreateDirectory(Path.Combine(contents, "MacOS"));
        Directory.CreateDirectory(Path.Combine(contents, "MonoBundle"));
        var resources = Directory.CreateDirectory(Path.Combine(contents, "Resources")).FullName;
        var source = Path.Combine(directory, "main.c");
        var marker = Path.Combine(directory, "restarted").Replace("\\", "\\\\").Replace("\"", "\\\"");
        File.WriteAllText(source, $$"""
            #include <stdio.h>
            int main(int argc, char **argv) {
                FILE *f = fopen("{{marker}}", "w");
                if (f) {
                    fprintf(f, "%s\n", "{{version}}");
                    for (int i = 1; i < argc; i++)
                        fprintf(f, "%s\n", argv[i]);
                    fclose(f);
                }
                return 0;
            }
            """);
        await MacCommand.RunAsync("/usr/bin/clang", [source, "-o", Path.Combine(contents, "MacOS", MacDmgPackage.ExecutableName)], Token);
        File.WriteAllText(Path.Combine(contents, "Info.plist"), """
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0"><dict>
            <key>CFBundleIdentifier</key><string>com.syncclipboard.updater-test</string>
            <key>CFBundleExecutable</key><string>SyncClipboard.Desktop.MacOS</string>
            <key>CFBundlePackageType</key><string>APPL</string>
            </dict></plist>
            """);
        File.WriteAllText(Path.Combine(contents, "MonoBundle", "update_info.json"), """
            {"UpdateInfo":{"manage_type":"manual","update_src":"github","package_name":"update.dmg"}}
            """);
        File.WriteAllText(Path.Combine(resources, "version"), version);
        File.CreateSymbolicLink(Path.Combine(resources, "current"), "version");
        if (version == "old")
            File.WriteAllText(Path.Combine(resources, "old-only"), "old");
        await MacCommand.RunAsync("/usr/bin/codesign", ["--force", "--deep", "--sign", "-", bundle], Token);
        return bundle;
    }

    private sealed class Interaction : IUpdateInteraction
    {
        public UpdateResult? Result { get; private set; }
        public Action<string>? OnReport { get; init; }
        public Action<bool>? OnRollbackAvailability { get; init; }
        public void SetRollbackAvailable(bool available) => OnRollbackAvailability?.Invoke(available);
        public UpdateFailureHandler? OnFailure { get; init; }
        public void Report(string phase, int percent) => OnReport?.Invoke(phase);
        public Task<ForceExitAction> ConfirmForceExitAsync(CancellationToken token) => Task.FromResult(ForceExitAction.No);
        public Task<UpdateFailureAction> AskFailureActionAsync(string path, Exception error, bool canRollback, CancellationToken token)
            => OnFailure?.Invoke(path, error, canRollback, token) ?? throw new AssertFailedException(path + ": " + error);
        public Task ShowResultAsync(UpdateResult result)
        {
            Result = result;
            return Task.CompletedTask;
        }
    }
}
