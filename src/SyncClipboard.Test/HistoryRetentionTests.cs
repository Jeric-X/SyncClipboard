using Microsoft.EntityFrameworkCore;
using Moq;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Server.Core.Models;
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

    [TestMethod]
    [DataRow(10, 1, 10, true, false)]
    [DataRow(1, 11, 10, true, false)]
    [DataRow(-10, 11, 10, false, true)]
    [DataRow(1, 1, 10, false, true)]
    [DataRow(0, 10, 10, false, false)]
    public async Task RemoteSync_PreservesActivityTimesWhenLocalStateWins(
        int localMinutesAhead, int localVersion, int remoteVersion, bool localWins, bool remoteWins)
    {
        await using var f = await Fixture.CreateAsync(Token);
        var remoteModified = DateTime.UtcNow.AddDays(-1);
        var localModified = remoteModified.AddMinutes(localMinutesAhead);
        var localAccessed = remoteModified.AddHours(1);
        var record = Record("state", localModified, localAccessed);
        record.Version = localVersion;
        record.Stared = true;
        record.Pinned = true;
        record.SyncStatus = HistorySyncStatus.Synced;
        f.Db.HistoryRecords.Add(record);
        await f.Db.SaveChangesAsync(Token);
        var remote = new HistoryRecordDto
        {
            Type = record.Type,
            Hash = record.Hash,
            Text = record.Text,
            CreateTime = record.Timestamp,
            LastModified = remoteModified,
            LastAccessed = remoteModified.AddHours(-1),
            Version = remoteVersion,
            IsDeleted = true
        };

        await f.Manager.SyncRemoteHistoryAsync([remote], Token);

        var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual(localWins ? localModified : remoteModified, saved.LastModified);
        Assert.AreEqual(localWins ? localAccessed : remote.LastAccessed.UtcDateTime, saved.LastAccessed);
        Assert.AreEqual(localWins ? HistorySyncStatus.NeedSync : HistorySyncStatus.Synced, saved.SyncStatus);
        Assert.AreEqual(remoteWins ? remoteVersion : localVersion, saved.Version);
        Assert.AreEqual(remoteWins, saved.IsDeleted);
        Assert.AreEqual(!remoteWins, saved.Stared);
        Assert.AreEqual(!remoteWins, saved.Pinned);

        // The same remote response must not erase a still-pending local update on a later pull.
        await f.Manager.SyncRemoteHistoryAsync([remote], Token);
        saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual(localWins ? localModified : remoteModified, saved.LastModified);
        Assert.AreEqual(localWins ? HistorySyncStatus.NeedSync : HistorySyncStatus.Synced, saved.SyncStatus);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DeletedAttachmentCleanup_PreservesTombstonesUntilExpiration(bool transferOnly)
    {
        await using var f = await Fixture.CreateAsync(Token);
        f.RuntimeConfig.EnableSyncHistory = true;
        var recent = DateTime.UtcNow.AddDays(-1);
        var expired = DateTime.UtcNow.AddDays(-31);
        var recentFile = Record(new string('A', 64), recent, recent);
        var expiredFile = Record(new string('B', 64), expired, expired);
        var recentText = Record("recent-text", recent, recent);
        var expiredText = Record("expired-text", expired, expired);
        var active = Record(new string('C', 64), expired, expired);
        var attachments = new[] { recentFile, expiredFile, active };
        foreach (var record in attachments)
        {
            record.Type = ProfileType.File;
            var directory = Profile.CreateWorkingDir(f.GetHistoryPersistentDir(), record.Type, record.Hash);
            await File.WriteAllTextAsync(Path.Combine(directory, "data.bin"), "content", Token);
            record.FilePath = transferOnly ? [] : ["data.bin"];
            record.TransferDataFile = "data.bin";
            record.TransferDataHash = new string('D', 64);
        }
        foreach (var record in new[] { recentFile, expiredFile, recentText, expiredText })
        {
            record.IsDeleted = true;
            record.Version = 7;
            record.SyncStatus = HistorySyncStatus.Synced;
        }
        f.Db.HistoryRecords.AddRange(recentFile, expiredFile, recentText, expiredText, active);
        await f.Db.SaveChangesAsync(Token);

        await f.Manager.ClearDeletedHistoryData(Token);

        var rows = await f.Db.HistoryRecords.AsNoTracking().ToListAsync(Token);
        Assert.HasCount(5, rows);
        foreach (var original in new[] { recentFile, expiredFile })
        {
            var saved = rows.Single(row => row.ID == original.ID);
            Assert.IsTrue(saved.IsDeleted);
            Assert.AreEqual(7, saved.Version);
            Assert.AreEqual(HistorySyncStatus.Synced, saved.SyncStatus);
            Assert.AreEqual(original.ID == recentFile.ID ? recent : expired, saved.LastModified);
            Assert.AreEqual(saved.LastModified, saved.LastAccessed);
            Assert.IsFalse(saved.IsLocalFileReady);
            Assert.IsEmpty(saved.FilePath);
            Assert.IsNull(saved.TransferDataFile);
            Assert.IsNull(saved.TransferDataHash);
            Assert.IsFalse(Directory.Exists(f.Manager.GetRecordWorkingDir(saved)));
        }
        var activePath = Path.Combine(f.Manager.GetRecordWorkingDir(active)!, "data.bin");
        Assert.AreEqual("content", await File.ReadAllTextAsync(activePath, Token));
        Assert.IsTrue(rows.Single(row => row.ID == active.ID).IsLocalFileReady);

        await f.Manager.ClearDeletedHistoryData(Token);
        await f.Manager.CleanupExpiredHistory(Token);

        var remaining = await f.Db.HistoryRecords.Select(row => row.Hash).ToListAsync(Token);
        CollectionAssert.AreEquivalent(new[] { recentFile.Hash, recentText.Hash, active.Hash }, remaining);
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
        public RuntimeHistoryConfig RuntimeConfig { get; } = new();

        private Fixture()
        {
            // Exercise real cleanup against SQLite without opening the application database or starting config listeners.
            Manager = (HistoryManager)RuntimeHelpers.GetUninitializedObject(typeof(HistoryManager));
            SetField("_dbContext", Db);
            SetField("_dbSemaphore", semaphore);
            SetField("_profileEnv", this);
            SetField("_logger", Mock.Of<ILogger>());
            SetField("_historyConfig", Config);
            SetField("_runtimeHistoryConfig", RuntimeConfig);
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
