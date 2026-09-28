using SyncClipboard.Core.Utilities.Updater;
using System.Security.Cryptography;
using System.Text.Json;

namespace SyncClipboard.Test;

[TestClass]
public class AppImageUpdateRunnerTests
{
    private string root = null!;
    private PreparedUpdate update = null!;

    [TestInitialize]
    public void Initialize()
    {
        root = Directory.CreateTempSubdirectory("AppImage update 中文 ' ").FullName;
        var work = Directory.CreateDirectory(Path.Combine(root, "task")).FullName;
        var target = Path.Combine(Directory.CreateDirectory(Path.Combine(root, "installed")).FullName, "SyncClipboard.AppImage");
        var stage = Path.Combine(work, "payload");
        File.WriteAllText(target, "old version");
        File.WriteAllText(stage, "new version");
        update = new PreparedUpdate
        {
            Directory = work,
            Kind = "AppImage",
            Target = target,
            Stage = stage,
            Backup = Path.Combine(work, "backup"),
            Executable = target,
            HelperExecutable = Path.Combine(work, "SyncClipboard-helper.AppImage"),
            Version = "v9.0.0",
            ProcessId = int.MaxValue,
            Digest = "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(stage)))
        };
        File.WriteAllText(Path.Combine(work, "commit"), "");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    [TestMethod]
    public async Task Replacement_ReportsProgressAndKeepsFilesInTaskDirectory()
    {
        var phases = new List<UpdateInstallProgress>();
        var launched = new List<string>();
        var runner = new AppImageUpdateRunner(update, launched.Add, TimeSpan.FromSeconds(1));
        Assert.IsTrue(await runner.RunAsync(new InlineProgress(phases.Add), CancellationToken.None));
        Assert.AreEqual("new version", File.ReadAllText(update.Target));
        Assert.AreEqual("old version", File.ReadAllText(update.Backup));
        CollectionAssert.AreEqual(new[] { update.Target }, Directory.GetFiles(Path.GetDirectoryName(update.Target)!));
        CollectionAssert.AreEqual(new[] { update.Target }, launched);
        Assert.IsTrue(phases.Any(p => p.Phase == "backup" && p.Percent == 100));
        Assert.IsTrue(phases.Any(p => p.Phase == "installing" && p.Percent == 100));
        Assert.IsTrue(File.Exists(Path.Combine(update.Directory, "completed")));
        Assert.IsFalse(File.Exists(Path.Combine(update.Directory, "ack")));
        if (!OperatingSystem.IsWindows()) Assert.AreNotEqual((UnixFileMode)0, File.GetUnixFileMode(update.Target) & UnixFileMode.UserExecute);
    }

    [TestMethod]
    public async Task ReplacementFailure_RestoresOldFileAndKeepsBackup()
    {
        var launched = new List<string>();
        var progress = new InlineProgress(p => { if (p.Phase == "backup" && p.Percent == 100) File.Delete(update.Stage); });
        var runner = new AppImageUpdateRunner(update, launched.Add, TimeSpan.FromSeconds(1));
        Assert.IsFalse(await runner.RunAsync(progress, CancellationToken.None));
        Assert.AreEqual("old version", File.ReadAllText(update.Target));
        Assert.AreEqual("old version", File.ReadAllText(update.Backup));
        Assert.IsTrue(File.Exists(Path.Combine(update.Directory, "restored")));
        Assert.IsTrue(File.Exists(Path.Combine(update.Directory, "failed")));
        CollectionAssert.AreEqual(new[] { update.Target }, launched);
    }

    [TestMethod]
    public async Task CorruptPayload_LeavesApplicationRunningAndDoesNotOfferHandoff()
    {
        File.WriteAllText(update.Stage, "corrupted");
        var runner = new AppImageUpdateRunner(update, _ => Assert.Fail("Should not restart"), TimeSpan.FromSeconds(1));
        Assert.IsFalse(await runner.RunAsync(new InlineProgress(_ => { }), CancellationToken.None));
        Assert.AreEqual("old version", File.ReadAllText(update.Target));
        Assert.IsFalse(File.Exists(Path.Combine(update.Directory, "ready")));
        Assert.IsFalse(File.Exists(update.Backup));
    }

    [TestMethod]
    public async Task ParentExitTimeout_DoesNotReplaceOrTerminateParent()
    {
        var runner = new AppImageUpdateRunner(update with { ProcessId = Environment.ProcessId },
            _ => Assert.Fail("Should not restart"), TimeSpan.FromMilliseconds(50));
        Assert.IsFalse(await runner.RunAsync(new InlineProgress(_ => { }), CancellationToken.None));
        Assert.AreEqual("old version", File.ReadAllText(update.Target));
        Assert.Contains("Timed out", File.ReadAllText(Path.Combine(update.Directory, "failed")));
        Assert.IsFalse(File.Exists(update.Backup));
    }

    [TestMethod]
    [DataRow("installing", 0)]
    [DataRow("installing", 100)]
    [DataRow("starting", -1)]
    public async Task CancelDuringInstallation_RestoresOriginal(string phase, int percent)
    {
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(p =>
        {
            if (p.Phase == phase && (percent < 0 || p.Percent == percent)) cancellation.Cancel();
        });
        var launched = new List<string>();
        var runner = new AppImageUpdateRunner(update, launched.Add, TimeSpan.FromSeconds(1));
        Assert.IsFalse(await runner.RunAsync(progress, cancellation.Token));
        Assert.AreEqual("old version", File.ReadAllText(update.Target));
        Assert.IsTrue(File.Exists(Path.Combine(update.Directory, "restored")));
        Assert.IsTrue(File.Exists(Path.Combine(update.Directory, "failed")));
        CollectionAssert.AreEqual(new[] { update.Target }, launched);
    }

    [TestMethod]
    public async Task TargetDeletionFailure_RestartsUntouchedApplication()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Requires Windows file sharing to block deletion.");
        FileStream? locked = null;
        try
        {
            var progress = new InlineProgress(p =>
            {
                if (p.Phase == "backup" && p.Percent == 100)
                    locked = new FileStream(update.Target, FileMode.Open, FileAccess.Read, FileShare.Read);
            });
            var launched = new List<string>();
            var runner = new AppImageUpdateRunner(update, launched.Add, TimeSpan.FromSeconds(1));
            Assert.IsFalse(await runner.RunAsync(progress, CancellationToken.None));
            Assert.AreEqual("old version", File.ReadAllText(update.Target));
            Assert.AreEqual("old version", File.ReadAllText(update.Backup));
            CollectionAssert.AreEqual(new[] { update.Target }, launched);
            Assert.IsFalse(File.ReadAllText(Path.Combine(update.Directory, "failed")).Contains("Rollback failed"));
        }
        finally { locked?.Dispose(); }
    }

    [TestMethod]
    public void TaskLoading_RejectsExternalBackupLocation()
    {
        var path = Path.Combine(update.Directory, "task.json");
        File.WriteAllText(path, JsonSerializer.Serialize(update with { Backup = update.Target + ".backup" }));
        Assert.Throws<InvalidDataException>(() => AppImageUpdateRunner.Load(path));
    }

    [TestMethod]
    public void HelperLaunch_UsesSeparateAppImageWithLiteralTaskArgument()
    {
        var start = UpdateInstallHelper.CreateStartInfo(update);
        Assert.AreEqual(update.HelperExecutable, start.FileName);
        CollectionAssert.AreEqual(new[] { "--install-update", Path.Combine(update.Directory, "task.json") }, start.ArgumentList.ToArray());
        Assert.IsFalse(start.Environment.ContainsKey("APPIMAGE"));
        Assert.IsFalse(start.Environment.ContainsKey("APPDIR"));
        Assert.IsFalse(start.Environment.ContainsKey("LD_LIBRARY_PATH"));
    }

    private sealed class InlineProgress(Action<UpdateInstallProgress> action) : IProgress<UpdateInstallProgress>
    {
        public void Report(UpdateInstallProgress value) => action(value);
    }
}
