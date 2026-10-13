using Microsoft.EntityFrameworkCore;
using Moq;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Shared.Models;
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

    private static readonly string[] ExpectedAttachmentPaths = ["data.bin"];

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
    [DataRow(10, 1, 10, true, false, -1)]
    [DataRow(10, 1, 10, true, false, 2)]
    [DataRow(1, 11, 10, true, false, -1)]
    [DataRow(1, 11, 10, true, false, 2)]
    [DataRow(-10, 11, 10, false, true, -1)]
    [DataRow(-10, 11, 10, false, true, 2)]
    [DataRow(1, 1, 10, false, true, -1)]
    [DataRow(0, 10, 10, false, false, -1)]
    [DataRow(1, 10, 10, false, false, -1)]
    [DataRow(-1, 10, 10, false, false, 2)]
    public async Task RemoteSync_PreservesActivityTimesWhenLocalStateWins(
        int localMinutesAhead, int localVersion, int remoteVersion, bool localWins, bool remoteWins, int remoteAccessHours)
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
            LastAccessed = remoteModified.AddHours(remoteAccessHours),
            Version = remoteVersion,
            IsDeleted = true
        };

        await f.Manager.SyncRemoteHistoryAsync([remote], Token);

        var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual(remoteWins ? remoteModified : localModified, saved.LastModified);
        Assert.AreEqual(remoteAccessHours > 1 ? remote.LastAccessed.UtcDateTime : localAccessed, saved.LastAccessed);
        Assert.AreEqual(localWins ? HistorySyncStatus.NeedSync : HistorySyncStatus.Synced, saved.SyncStatus);
        Assert.AreEqual(remoteWins ? remoteVersion : localVersion, saved.Version);
        Assert.AreEqual(remoteWins, saved.IsDeleted);
        Assert.AreEqual(!remoteWins, saved.Stared);
        Assert.AreEqual(!remoteWins, saved.Pinned);

        // The same remote response must not erase a still-pending local update on a later pull.
        await f.Manager.SyncRemoteHistoryAsync([remote], Token);
        saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual(remoteWins ? remoteModified : localModified, saved.LastModified);
        Assert.AreEqual(localWins ? HistorySyncStatus.NeedSync : HistorySyncStatus.Synced, saved.SyncStatus);
    }

    [TestMethod]
    [DataRow(9, -1)]
    [DataRow(9, 1)]
    [DataRow(10, -1)]
    [DataRow(10, 1)]
    [DataRow(11, -1)]
    [DataRow(11, 1)]
    public async Task RemoteSync_PreservesEarliestCreationTime(int remoteVersion, int remoteCreatedDays)
    {
        await using var f = await Fixture.CreateAsync(Token);
        var activityTime = DateTime.UtcNow.AddDays(-1);
        var record = Record("creation-time", activityTime, activityTime);
        record.Version = 10;
        f.Db.HistoryRecords.Add(record);
        await f.Db.SaveChangesAsync(Token);
        var localCreated = record.Timestamp;
        var remote = record.ToHistoryRecordDto();
        remote.Version = remoteVersion;
        remote.CreateTime = localCreated.AddDays(remoteCreatedDays);

        await f.Manager.SyncRemoteHistoryAsync([remote], Token);

        var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual(remoteCreatedDays < 0 ? remote.CreateTime.UtcDateTime : localCreated, saved.Timestamp);

        // Repeated synchronization must not move the oldest known creation time forward.
        remote.CreateTime = localCreated.AddDays(2);
        await f.Manager.SyncRemoteHistoryAsync([remote], Token);
        saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual(remoteCreatedDays < 0 ? localCreated.AddDays(remoteCreatedDays) : localCreated, saved.Timestamp);
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

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CopyExistingRecord_RestorationIsAddedAndPendingUpload(bool deleted)
    {
        await using var f = await Fixture.CreateAsync(Token);
        var profile = new TextProfile("restore copied text");
        var old = DateTime.UtcNow.AddDays(-2);
        var record = Record(await profile.GetHash(Token), old, old);
        record.Text = profile.DisplayText;
        record.IsDeleted = deleted;
        record.SyncStatus = HistorySyncStatus.Synced;
        record.Version = 7;
        record.Stared = true;
        var timestamp = record.Timestamp;
        f.Db.HistoryRecords.Add(record);
        await f.Db.SaveChangesAsync(Token);
        var added = new List<HistoryRecord>();
        var updated = new List<HistoryRecord>();
        f.Manager.HistoryAdded += added.Add;
        f.Manager.HistoryUpdated += updated.Add;
        var before = DateTime.UtcNow;

        await f.Manager.AddLocalProfile(profile, token: Token);

        var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.IsFalse(saved.IsDeleted);
        Assert.AreEqual(deleted ? 8 : 7, saved.Version);
        Assert.AreEqual(deleted ? HistorySyncStatus.LocalOnly : HistorySyncStatus.Synced, saved.SyncStatus);
        Assert.IsTrue(saved.LastModified >= before);
        Assert.IsTrue(saved.LastAccessed >= before);
        Assert.AreEqual(timestamp, saved.Timestamp);
        Assert.IsTrue(saved.Stared);
        Assert.HasCount(deleted ? 1 : 0, added);
        Assert.HasCount(deleted ? 0 : 1, updated);
        Assert.AreEqual(saved.ID, (deleted ? added : updated).Single().ID);
    }

    [TestMethod]
    [DataRow(false, false, false, false)]
    [DataRow(true, false, false, false)]
    [DataRow(true, true, false, false)]
    [DataRow(false, false, true, false)]
    [DataRow(false, false, true, true)]
    public async Task Upload_SkipsActiveRecordsOrPersistsAcceptedResponse(
        bool remoteExists, bool remoteDeleted, bool concurrentRemoteRecord, bool remoteAccessedLater)
    {
        await using var f = await Fixture.CreateAsync(Token);
        var source = Path.Combine(f.GetPersistentDir(), "source.txt");
        await File.WriteAllTextAsync(source, "attachment content", Token);
        var profile = new FileProfile(source);
        await f.Manager.AddLocalProfile(profile, token: Token);
        var local = await f.Db.HistoryRecords.SingleAsync(Token);
        local.Version = 8;
        local.LastModified = DateTime.UtcNow.AddMinutes(-1);
        await f.Db.SaveChangesAsync(Token);
        var modified = local.LastModified;
        var accessed = local.LastAccessed;
        var created = local.Timestamp;
        var accepted = local.ToHistoryRecordDto();
        accepted.Version = 21;
        if (concurrentRemoteRecord)
        {
            accepted.Starred = true;
            accepted.Pinned = true;
            accepted.LastModified = modified.AddMinutes(1);
            accepted.LastAccessed = accessed.AddMinutes(remoteAccessedLater ? 1 : -1);
            accepted.CreateTime = created.AddDays(remoteAccessedLater ? 1 : -1);
        }
        var remote = new Mock<IRemoteClipboardServer>();
        var server = remote.As<IOfficialSyncServer>();
        server.Setup(x => x.GetHistoryByProfileIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(remoteExists ? new HistoryRecordDto
            {
                Type = local.Type,
                Hash = local.Hash,
                IsDeleted = remoteDeleted,
                Version = 20,
                HasData = true
            } : null);
        HistoryRecordDto? uploaded = null;
        string? uploadedContent = null;
        server.Setup(x => x.UploadHistoryAsync(It.IsAny<HistoryRecordDto>(), It.IsAny<FileHashInfo?>(),
            It.IsAny<IProgress<HttpDownloadProgress>?>(), It.IsAny<CancellationToken>()))
            .Returns<HistoryRecordDto, FileHashInfo?, IProgress<HttpDownloadProgress>?, CancellationToken>(
                async (dto, file, _, token) =>
                {
                    uploaded = dto;
                    Assert.IsNotNull(file);
                    uploadedContent = await File.ReadAllTextAsync(file.Path, token);
                    Assert.AreEqual(await Utility.CalculateFileSHA256(file.Path, token), file.Hash);
                    return accepted;
                });
        var factory = (RemoteClipboardServerFactory)RuntimeHelpers.GetUninitializedObject(typeof(RemoteClipboardServerFactory));
        typeof(RemoteClipboardServerFactory).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(factory, remote.Object);
        typeof(RemoteClipboardServerFactory).GetField("_serverStateLock", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(factory, RuntimeHelpers.GetUninitializedObject(typeof(Lock)));
        using var queue = new HistoryTransferQueue(Mock.Of<ILogger>(), factory, f.Manager, f, null!, new ConfigBase());

        await queue.Upload(profile, ct: Token);

        if (remoteExists && !remoteDeleted)
        {
            Assert.IsNull(uploaded);
        }
        else
        {
            Assert.IsNotNull(uploaded);
            Assert.IsFalse(uploaded.IsDeleted);
            Assert.AreEqual(8, uploaded.Version);
            Assert.AreEqual(modified, uploaded.LastModified.UtcDateTime);
            Assert.AreEqual("attachment content", uploadedContent);
        }
        var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual(HistorySyncStatus.Synced, saved.SyncStatus);
        Assert.AreEqual(remoteExists && !remoteDeleted ? 8 : 21, saved.Version);
        Assert.AreEqual(concurrentRemoteRecord ? accepted.LastModified.UtcDateTime : modified, saved.LastModified);
        Assert.AreEqual(concurrentRemoteRecord, saved.Stared);
        Assert.AreEqual(concurrentRemoteRecord, saved.Pinned);
        Assert.IsFalse(saved.IsDeleted);
        Assert.AreEqual(remoteAccessedLater ? accepted.LastAccessed.UtcDateTime : accessed, saved.LastAccessed);
        Assert.AreEqual(concurrentRemoteRecord && !remoteAccessedLater ? accepted.CreateTime.UtcDateTime : created,
            saved.Timestamp);
        Assert.AreEqual(accepted.Text, saved.Text);
        Assert.AreEqual(accepted.Size, saved.Size);
        Assert.IsTrue(saved.IsLocalFileReady);
        Assert.IsGreaterThan(0, saved.FilePath.Length);
    }

    [TestMethod]
    [TestCategory("PlatformWindows")]
    public async Task DeletedAttachmentCleanup_RetriesAfterLockedFileIsReleased()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows file sharing is required to prevent deletion.");
        }
        await AssertDeletionRetryAsync(lockFile: true);
    }

    [TestMethod]
    [TestCategory("PlatformMacOS")]
    [TestCategory("PlatformLinux")]
    public async Task DeletedAttachmentCleanup_RetriesAfterDirectoryPermissionIsRestored()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("Unix directory permissions are required to prevent deletion.");
        }
        await AssertDeletionRetryAsync(lockFile: false);
    }

    private async Task AssertDeletionRetryAsync(bool lockFile)
    {
        await using var f = await Fixture.CreateAsync(Token);
        var old = DateTime.UtcNow.AddDays(-1);
        var record = Record(new string('E', 64), old, old);
        record.Type = ProfileType.File;
        record.IsDeleted = true;
        record.SyncStatus = HistorySyncStatus.Synced;
        record.FilePath = ["data.bin"];
        record.TransferDataFile = "data.bin";
        record.TransferDataHash = new string('F', 64);
        var directory = Profile.CreateWorkingDir(f.GetHistoryPersistentDir(), record.Type, record.Hash);
        var path = Path.Combine(directory, "data.bin");
        await File.WriteAllTextAsync(path, "content", Token);
        f.Db.HistoryRecords.Add(record);
        await f.Db.SaveChangesAsync(Token);
        FileStream? held = null;
        UnixFileMode? originalMode = null;
        try
        {
            if (lockFile)
            {
                held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            else if (!OperatingSystem.IsWindows())
            {
                originalMode = File.GetUnixFileMode(directory);
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            }
            await f.Manager.ClearDeletedHistoryData(Token);
            Assert.IsTrue(Directory.Exists(directory));
            var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
            Assert.IsTrue(saved.IsLocalFileReady);
            CollectionAssert.AreEqual(ExpectedAttachmentPaths, saved.FilePath);
            Assert.AreEqual("data.bin", saved.TransferDataFile);
            Assert.AreEqual(new string('F', 64), saved.TransferDataHash);
            Assert.IsTrue(saved.IsDeleted);
        }
        finally
        {
            held?.Dispose();
            if (originalMode.HasValue && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(directory, originalMode.Value);
            }
        }
        await f.Manager.ClearDeletedHistoryData(Token);
        Assert.IsFalse(Directory.Exists(directory));
        var cleaned = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.IsFalse(cleaned.IsLocalFileReady);
        Assert.IsEmpty(cleaned.FilePath);
        Assert.IsNull(cleaned.TransferDataFile);
        Assert.IsNull(cleaned.TransferDataHash);
        Assert.IsTrue(cleaned.IsDeleted);
        Assert.AreEqual(old, cleaned.LastModified);
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
