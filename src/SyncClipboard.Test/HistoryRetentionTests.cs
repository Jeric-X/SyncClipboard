using Microsoft.EntityFrameworkCore;
using Moq;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Utilities;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SyncClipboard.Test;

[TestClass]
public class HistoryRetentionTests
{
    public TestContext TestContext { get; set; } = null!;
    private CancellationToken Token => TestContext.CancellationTokenSource.Token;

    [TestMethod]
    public async Task Expiration_RequiresBothActivityTimesToBeOldAndPreservesProtectedRecords()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var old = DateTime.UtcNow.AddDays(-10);
        var recent = DateTime.UtcNow;
        var accessed = Record("accessed", old, recent);
        var modified = Record("modified", recent, old);
        var expired = Record("expired", old, old);
        expired.Timestamp = recent;
        var starred = Record("starred", old, old);
        starred.Stared = true;
        var pinned = Record("pinned", old, old);
        pinned.Pinned = true;
        var synced = Record("synced", old, old);
        synced.SyncStatus = HistorySyncStatus.Synced;
        var pending = Record("pending", old, old);
        pending.SyncStatus = HistorySyncStatus.NeedSync;
        f.Db.HistoryRecords.AddRange(accessed, modified, expired, starred, pinned, synced, pending);
        await f.Db.SaveChangesAsync(Token);

        await f.Manager.CleanupExpiredHistory(Token);

        var hashes = await f.Db.HistoryRecords.Select(r => r.Hash).ToListAsync(Token);
        string[] expected = ["accessed", "modified", "starred", "pinned", "synced", "pending"];
        CollectionAssert.AreEquivalent(expected, hashes);
        await f.Db.Entry(accessed).ReloadAsync(Token);
        await f.Db.Entry(modified).ReloadAsync(Token);
        Assert.AreEqual(old, accessed.LastModified);
        Assert.AreEqual(recent, accessed.LastAccessed);
        Assert.AreEqual(recent, modified.LastModified);
        Assert.AreEqual(old, modified.LastAccessed);
    }

    [TestMethod]
    public async Task CountLimit_UsesLatestActivityInsteadOfCreationTime()
    {
        await using var f = await Fixture.CreateAsync(Token);
        f.Config.MaxItemCount = 3;
        var old = DateTime.UtcNow.AddDays(-10);
        var recent = DateTime.UtcNow;
        var inactive = Record("inactive", old, old);
        inactive.Timestamp = recent;
        var starred = Record("starred", old.AddDays(-1), old.AddDays(-1));
        starred.Stared = true;
        f.Db.HistoryRecords.AddRange(
            Record("accessed", old, recent), Record("modified", recent, old), inactive, starred);
        await f.Db.SaveChangesAsync(Token);

        await f.Manager.EnforceMaxItemCountAsync(Token);

        var hashes = await f.Db.HistoryRecords.Select(r => r.Hash).ToListAsync(Token);
        string[] expected = ["accessed", "modified", "starred"];
        CollectionAssert.AreEquivalent(expected, hashes);
    }

    private static HistoryRecord Record(string hash, DateTime modified, DateTime accessed) => new()
    {
        Type = ProfileType.Text,
        Hash = hash,
        Text = hash,
        Timestamp = DateTime.UtcNow.AddDays(-20),
        LastModified = modified,
        LastAccessed = accessed
    };

    private sealed class MemoryHistoryDbContext : HistoryDbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite("Data Source=:memory:");
    }

    private sealed class Fixture : IProfileEnv, IAsyncDisposable
    {
        private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("SyncClipboard-Retention-");
        private readonly SemaphoreSlim semaphore = new(1, 1);
        public MemoryHistoryDbContext Db { get; } = new();
        public HistoryConfig Config { get; } = new() { HistoryRetentionMinutes = 60, MaxItemCount = 0 };
        public HistoryManager Manager { get; }

        private Fixture()
        {
            // Exercise real cleanup against SQLite without opening the application database or starting config listeners.
            Manager = (HistoryManager)RuntimeHelpers.GetUninitializedObject(typeof(HistoryManager));
            SetField("_dbContext", Db);
            SetField("_dbSemaphore", semaphore);
            SetField("_profileEnv", this);
            SetField("_logger", Mock.Of<ILogger>());
            SetField("_historyConfig", Config);
            SetField("_runtimeHistoryConfig", new RuntimeHistoryConfig());
            SetField("_historyManagerHelper", new HistoryManagerHelper<HistoryRecord, DateTime>(Manager));
            Manager.EnableCleanup = true;
        }

        private void SetField(string name, object value) => typeof(HistoryManager)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Manager, value);

        public static async Task<Fixture> CreateAsync(CancellationToken token)
        {
            var fixture = new Fixture();
            await fixture.Db.Database.OpenConnectionAsync(token);
            await fixture.Db.Database.EnsureCreatedAsync(token);
            return fixture;
        }

        public string GetPersistentDir() => root.FullName;
        public string GetHistoryPersistentDir() => root.FullName;

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            semaphore.Dispose();
            root.Delete(true);
        }
    }
}
