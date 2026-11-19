using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Models;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Shared.Profiles;

namespace SyncClipboard.Test.Client.History;

[TestClass]
public class HistoryDatabaseOperationTests
{
    public TestContext TestContext { get; set; } = null!;
    private CancellationToken Token => TestContext.CancellationTokenSource.Token;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedEdit_DoesNotLeakIntoLaterSaveAndCanRetrySameValue(bool tags)
    {
        await using var fixture = await HistoryRetentionTests.Fixture.CreateAsync(Token);
        var record = new HistoryRecord
        {
            Type = ProfileType.Text,
            Hash = "ABC",
            Title = "before",
            Tags = ["before"],
            Version = 7,
            SyncStatus = HistorySyncStatus.Synced
        };
        fixture.Db.Add(record);
        await fixture.Db.SaveChangesAsync(Token);
        var key = HistoryRecordKey.From(record);
        var notifications = new List<HistoryRecord>();
        fixture.Manager.HistoryUpdated += notifications.Add;
        await fixture.Db.Database.ExecuteSqlRawAsync(tags
            ? "CREATE TRIGGER reject_edit BEFORE UPDATE OF Tags ON HistoryRecords BEGIN SELECT RAISE(ABORT, 'rejected'); END;"
            : "CREATE TRIGGER reject_edit BEFORE UPDATE OF Title ON HistoryRecords BEGIN SELECT RAISE(ABORT, 'rejected'); END;", Token);

        async Task EditAsync()
        {
            if (tags)
                Assert.AreEqual(1, await fixture.Manager.EditTagsAsync([key], ["after"], ["before"], Token));
            else
                Assert.IsTrue(await fixture.Manager.SetTitleAsync(key, "after", Token));
        }

        await Assert.ThrowsExactlyAsync<DbUpdateException>(EditAsync);
        Assert.IsEmpty(notifications);
        var unchanged = await fixture.Manager.GetHistoryRecord(key.Hash, key.Type, Token);
        Assert.IsNotNull(unchanged);
        Assert.AreEqual("before", unchanged.Title);
        Assert.AreEqual("before", unchanged.Tags!.Single());
        Assert.AreEqual(7, unchanged.Version);
        Assert.AreEqual(record.LastModified, unchanged.LastModified);
        Assert.AreEqual(HistorySyncStatus.Synced, unchanged.SyncStatus);

        await fixture.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_edit;", Token);
        await fixture.Manager.SetStarredAsync([key], true, Token);
        var starred = await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.IsTrue(starred.Stared);
        Assert.AreEqual("before", starred.Title);
        Assert.AreEqual("before", starred.Tags!.Single());
        Assert.AreEqual(8, starred.Version);

        await EditAsync();
        var saved = await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual(tags ? "before" : "after", saved.Title);
        CollectionAssert.AreEqual(new[] { tags ? "after" : "before" }, saved.Tags);
        Assert.AreEqual(9, saved.Version);
        Assert.AreEqual(HistorySyncStatus.NeedSync, saved.SyncStatus);
        Assert.HasCount(2, notifications);
        Assert.AreEqual(8, notifications[0].Version, "Later edits must not mutate a previously published record.");
    }

    [TestMethod]
    public async Task FailedBatchEdit_RollsBackEveryRecordAndCanRetry()
    {
        await using var fixture = await HistoryRetentionTests.Fixture.CreateAsync(Token);
        var first = new HistoryRecord { Type = ProfileType.Text, Hash = "FIRST", Tags = ["old"] };
        var second = new HistoryRecord { Type = ProfileType.Text, Hash = "SECOND", Tags = ["old"] };
        fixture.Db.AddRange(first, second);
        await fixture.Db.SaveChangesAsync(Token);
        HistoryRecordKey[] keys = [HistoryRecordKey.From(first), HistoryRecordKey.From(second)];
        var notifications = 0;
        fixture.Manager.HistoryUpdated += _ => notifications++;
        await fixture.Db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER reject_batch BEFORE UPDATE ON HistoryRecords WHEN OLD.Hash = 'SECOND' BEGIN SELECT RAISE(ABORT, 'rejected'); END;", Token);

        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => fixture.Manager.EditTagsAsync(keys, ["new"], ["old"], Token));
        Assert.AreEqual(0, notifications);
        foreach (var record in await fixture.Manager.GetHistory(Token))
        {
            Assert.AreEqual("old", record.Tags!.Single());
            Assert.AreEqual(0, record.Version);
        }
        await fixture.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_batch;", Token);
        Assert.AreEqual(2, await fixture.Manager.EditTagsAsync(keys, ["new"], ["old"], Token));
        var saved = await fixture.Db.HistoryRecords.AsNoTracking().ToListAsync(Token);
        Assert.IsTrue(saved.All(record => record.Tags!.SequenceEqual(["new"]) && record.Version == 1));
        Assert.AreEqual(saved[0].LastModified, saved[1].LastModified);
        Assert.AreEqual(2, notifications);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedRemoteSave_DoesNotPublishOrLeakChanges(bool existing)
    {
        await using var fixture = await HistoryRetentionTests.Fixture.CreateAsync(Token);
        if (existing)
        {
            fixture.Db.Add(new HistoryRecord { Type = ProfileType.Text, Hash = "REMOTE", Title = "old" });
            await fixture.Db.SaveChangesAsync(Token);
        }
        var notifications = new List<HistoryRecord>();
        fixture.Manager.HistoryAdded += notifications.Add;
        fixture.Manager.HistoryUpdated += notifications.Add;
        await fixture.Db.Database.ExecuteSqlRawAsync(existing
            ? "CREATE TRIGGER reject_remote BEFORE UPDATE ON HistoryRecords BEGIN SELECT RAISE(ABORT, 'rejected'); END;"
            : "CREATE TRIGGER reject_remote BEFORE INSERT ON HistoryRecords BEGIN SELECT RAISE(ABORT, 'rejected'); END;", Token);
        var dto = new HistoryRecordDto
        {
            Type = ProfileType.Text,
            Hash = "REMOTE",
            Title = "new",
            Version = 8,
            LastModified = DateTimeOffset.UtcNow
        };

        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => fixture.Manager.SyncRemoteHistoryAsync([dto], Token));
        Assert.IsEmpty(notifications);
        var failed = await fixture.Manager.GetHistoryRecord(dto.Hash, dto.Type, Token);
        if (existing)
            Assert.AreEqual("old", failed!.Title);
        else
            Assert.IsNull(failed);
        await fixture.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_remote;", Token);
        await fixture.Manager.SyncRemoteHistoryAsync([dto], Token);
        var saved = await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual("new", saved.Title);
        Assert.AreEqual(8, saved.Version);
        Assert.HasCount(1, notifications);
        Assert.AreEqual(saved.ID, notifications[0].ID);
    }

    [TestMethod]
    public async Task ClearAll_ContinuesAfterOneRecordFailsToDelete()
    {
        await using var fixture = await HistoryRetentionTests.Fixture.CreateAsync(Token);
        fixture.Db.AddRange(
            new HistoryRecord { Type = ProfileType.Text, Hash = "FAILED" },
            new HistoryRecord { Type = ProfileType.Text, Hash = "DELETED", SyncStatus = HistorySyncStatus.Synced });
        await fixture.Db.SaveChangesAsync(Token);
        await fixture.Db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER reject_delete BEFORE DELETE ON HistoryRecords WHEN OLD.Hash = 'FAILED' BEGIN SELECT RAISE(ABORT, 'rejected'); END;", Token);
        var notifications = new List<HistoryRecord>();
        fixture.Manager.HistoryRemoved += notifications.Add;

        await fixture.Manager.ClearAllLocalAsync(Token);

        Assert.AreEqual("FAILED", (await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(Token)).Hash);
        Assert.AreEqual("DELETED", notifications.Single().Hash);
        Assert.AreEqual(HistorySyncStatus.LocalOnly, notifications.Single().SyncStatus);
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(true, true)]
    [DataRow(false, true)]
    public async Task LocalConversionAndCleanup_PreserveConcurrentStarEdit(bool convert, bool missingFile)
    {
        await using var fixture = await HistoryRetentionTests.Fixture.CreateAsync(Token);
        fixture.Config.AutoDeleteMissingLocalFiles = true;
        var first = new HistoryRecord
        {
            Type = ProfileType.Text,
            Hash = "FIRST",
            Timestamp = DateTime.UtcNow.AddMinutes(1),
            SyncStatus = convert ? HistorySyncStatus.Synced : HistorySyncStatus.LocalOnly,
            IsLocalFileReady = !missingFile
        };
        var second = new HistoryRecord
        {
            Type = ProfileType.Text,
            Hash = "SECOND",
            SyncStatus = first.SyncStatus,
            IsLocalFileReady = !missingFile
        };
        fixture.Db.AddRange(first, second);
        await fixture.Db.SaveChangesAsync(Token);
        Task edit = Task.CompletedTask;
        void StarSecondRecord(HistoryRecord record)
        {
            if (record.Hash == first.Hash)
                edit = fixture.Manager.SetStarredAsync([HistoryRecordKey.From(second)], true, Token);
        }
        fixture.Manager.HistoryUpdated += StarSecondRecord;
        fixture.Manager.HistoryRemoved += StarSecondRecord;

        if (convert)
            await fixture.Manager.ConvertToLocalRecordsAsync(Token);
        else
            await fixture.Manager.RemoveDeletedOrMissingLocalRecordsAsync(Token);
        await edit;

        var saved = await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(r => r.Hash == second.Hash, Token);
        Assert.IsTrue(saved.Stared);
        Assert.AreEqual(1, saved.Version);
        Assert.AreEqual(HistorySyncStatus.LocalOnly, saved.SyncStatus);
    }

    [TestMethod]
    public async Task ReturnedRecordMutation_DoesNotPersistThroughAnotherOperation()
    {
        await using var fixture = await HistoryRetentionTests.Fixture.CreateAsync(Token);
        fixture.Db.Add(new HistoryRecord { Type = ProfileType.Text, Hash = "ABC", Title = "saved", Tags = ["saved"] });
        await fixture.Db.SaveChangesAsync(Token);
        var record = (await fixture.Manager.GetHistory(Token)).Single();
        record.Title = "unsaved";
        record.Tags![0] = "unsaved";

        await fixture.Manager.SetStarredAsync([HistoryRecordKey.From(record)], true, Token);

        var saved = await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual("saved", saved.Title);
        Assert.AreEqual("saved", saved.Tags!.Single());
        Assert.IsTrue(saved.Stared);
    }
}
