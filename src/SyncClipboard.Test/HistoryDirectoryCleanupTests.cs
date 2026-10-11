using Microsoft.EntityFrameworkCore;
using Moq;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Utilities;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SyncClipboard.Test;

[TestClass]
public class HistoryDirectoryCleanupTests
{
    public TestContext TestContext { get; set; } = null!;
    private CancellationToken Token => TestContext.CancellationTokenSource.Token;

    private sealed class MemoryDb : HistoryDbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite("Data Source=:memory:");
    }

    private sealed class Fixture : IProfileEnv, IAsyncDisposable
    {
        private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("SyncClipboard-HistoryCleanup-");
        public MemoryDb Db { get; } = new();
        public SemaphoreSlim DbSemaphore { get; } = new(1, 1);
        public HistoryManager Manager { get; }
        public string GetPersistentDir() => Path.Combine(root.FullName, "clipboard");
        public string GetHistoryPersistentDir() => Path.Combine(root.FullName, "history");

        public Fixture()
        {
            Manager = (HistoryManager)RuntimeHelpers.GetUninitializedObject(typeof(HistoryManager));
            SetField("_dbContext", Db);
            SetField("_dbSemaphore", DbSemaphore);
            SetField("_logger", Mock.Of<ILogger>());
            SetField("_profileEnv", this);
        }

        private void SetField(string name, object value) => typeof(HistoryManager)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Manager, value);

        public async Task InitializeAsync(CancellationToken token)
        {
            await Db.Database.OpenConnectionAsync(token);
            await Db.Database.EnsureCreatedAsync(token);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            DbSemaphore.Dispose();
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task RemovingHistory_DeletesHistoryDirectoryAndPreservesClipboardDirectory()
    {
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var record = new HistoryRecord { Type = ProfileType.File, Hash = new string('A', 64) };
        f.Db.HistoryRecords.Add(record);
        await f.Db.SaveChangesAsync(Token);
        var history = Profile.CreateWorkingDir(f.GetHistoryPersistentDir(), record.Type, record.Hash);
        var clipboard = Profile.CreateWorkingDir(f.GetPersistentDir(), record.Type, record.Hash);
        await File.WriteAllTextAsync(Path.Combine(history, "data.txt"), "history", Token);
        var original = Path.Combine(clipboard, "data.txt");
        await File.WriteAllTextAsync(original, "clipboard", Token);

        await f.Manager.RemoveHistory(record, Token);

        Assert.IsFalse(Directory.Exists(history));
        Assert.AreEqual("clipboard", await File.ReadAllTextAsync(original, Token));
        Assert.IsEmpty(await f.Db.HistoryRecords.ToListAsync(Token));
    }

    [TestMethod]
    [TestCategory("PlatformMacOS")]
    [TestCategory("PlatformLinux")]
    [TestCategory("PlatformWindows")]
    public async Task OrphanCleanupWaitsForCommitAndPreservesItsAttachments()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
            Assert.Inconclusive("Requires a supported filesystem.");
        await using var f = new Fixture();
        await f.InitializeAsync(Token);
        var record = new HistoryRecord { Type = ProfileType.File, Hash = new string('A', 64) };
        var root = Profile.CreateWorkingDir(f.GetHistoryPersistentDir(), record.Type, record.Hash);
        var orphan = Directory.CreateDirectory(Path.Combine(f.GetHistoryPersistentDir(), "unused")).FullName;
        Directory.SetCreationTime(root, DateTime.Now.AddDays(-10));
        Directory.SetCreationTime(orphan, DateTime.Now.AddDays(-10));
        if (Directory.GetCreationTime(root) > DateTime.Now.AddDays(-7) || Directory.GetCreationTime(orphan) > DateTime.Now.AddDays(-7))
            Assert.Inconclusive("Filesystem cannot set directory creation time.");
        var attachment = Path.Combine(root, "data.txt");
        await File.WriteAllTextAsync(attachment, "history data", Token);
        record.FilePath = ["data.txt"];
        await f.DbSemaphore.WaitAsync(Token);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = Task.Run(() =>
        {
            started.SetResult();
            f.Manager.CleanupOrphanedHistoryFolders(Token);
        }, Token);
        try
        {
            await started.Task.WaitAsync(Token);
            await Task.Delay(100, Token);
            Assert.IsFalse(cleanup.IsCompleted, "Cleanup must wait for the in-flight database commit.");
            f.Db.HistoryRecords.Add(record);
            await f.Db.SaveChangesAsync(Token);
        }
        finally
        {
            f.DbSemaphore.Release();
        }
        await cleanup.WaitAsync(Token);
        Assert.IsTrue(File.Exists(attachment));
        Assert.IsFalse(Directory.Exists(orphan), "Old directories without a record still need cleanup.");
    }

}
