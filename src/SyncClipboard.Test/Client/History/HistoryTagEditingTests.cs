using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Utilities;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SyncClipboard.Test.Client.History;

[TestClass]
public class HistoryTagEditingTests
{
    public TestContext TestContext { get; set; } = null!;
    private CancellationToken Token => TestContext.CancellationTokenSource.Token;

    [TestMethod]
    [DataRow(HistorySyncStatus.LocalOnly)]
    [DataRow(HistorySyncStatus.Synced)]
    [DataRow(HistorySyncStatus.NeedSync)]
    public async Task BatchEdit_PersistsTagsAndSchedulesChangedRecordsForSync(HistorySyncStatus status)
    {
        await using var f = await Fixture.CreateAsync(Token);
        var first = f.Add("first", ["keep", "old"], status);
        var second = f.Add("second", ["other"], status);
        first.Stared = true;
        first.Pinned = true;
        var accessed = first.LastAccessed;
        var created = first.Timestamp;
        await f.Db.SaveChangesAsync(Token);
        var notified = new List<int>();
        f.Manager.HistoryUpdated += record =>
        {
            var saved = f.Db.HistoryRecords.AsNoTracking().Single(r => r.ID == record.ID);
            Assert.AreEqual(record.Version, saved.Version);
            CollectionAssert.AreEqual(record.Tags, saved.Tags);
            notified.Add(record.ID);
        };

        var changed = await f.Manager.EditTagsAsync(
            [HistoryRecordKey.From(first), HistoryRecordKey.From(second)], [" 工作 ", "工作"], ["old"], Token);

        Assert.AreEqual(2, changed);
        CollectionAssert.AreEquivalent(new[] { first.ID, second.ID }, notified);
        f.Db.ChangeTracker.Clear();
        var savedFirst = await f.Db.HistoryRecords.SingleAsync(r => r.ID == first.ID, Token);
        var savedSecond = await f.Db.HistoryRecords.SingleAsync(r => r.ID == second.ID, Token);
        string[] expectedFirst = ["keep", "工作"];
        string[] expectedSecond = ["other", "工作"];
        CollectionAssert.AreEqual(expectedFirst, savedFirst.Tags);
        CollectionAssert.AreEqual(expectedSecond, savedSecond.Tags);
        foreach (var saved in new[] { savedFirst, savedSecond })
        {
            Assert.AreEqual(8, saved.Version);
            Assert.IsGreaterThan(Fixture.OldTime, saved.LastModified);
            Assert.AreEqual(status == HistorySyncStatus.LocalOnly ? status : HistorySyncStatus.NeedSync, saved.SyncStatus);
        }
        Assert.IsTrue(savedFirst.Stared);
        Assert.IsTrue(savedFirst.Pinned);
        Assert.AreEqual(accessed, savedFirst.LastAccessed);
        Assert.AreEqual(created, savedFirst.Timestamp);
    }

    [TestMethod]
    public async Task BatchEdit_ChangesEachIdentityOnceAndSkipsMissingOrDeletedRecords()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var record = f.Add("ABC", []);
        var deleted = f.Add("deleted", ["kept"]);
        deleted.IsDeleted = true;
        await f.Db.SaveChangesAsync(Token);
        var notified = 0;
        f.Manager.HistoryUpdated += _ => notified++;

        var changed = await f.Manager.EditTagsAsync(
            [new(ProfileType.Text, "abc"), HistoryRecordKey.From(record), HistoryRecordKey.From(record),
             HistoryRecordKey.From(deleted), new(ProfileType.Text, "missing")], ["new"], [], Token);

        Assert.AreEqual(1, changed);
        Assert.AreEqual(1, notified);
        f.Db.ChangeTracker.Clear();
        Assert.AreEqual(8, (await f.Db.HistoryRecords.SingleAsync(r => r.ID == record.ID, Token)).Version);
        var savedDeleted = await f.Db.HistoryRecords.SingleAsync(r => r.ID == deleted.ID, Token);
        string[] expected = ["kept"];
        CollectionAssert.AreEqual(expected, savedDeleted.Tags);
        Assert.AreEqual(7, savedDeleted.Version);
    }

    [TestMethod]
    public async Task UnchangedTags_DoNotUpdateMetadataOrNotify()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var record = f.Add("unchanged", ["kept"], HistorySyncStatus.Synced);
        await f.Db.SaveChangesAsync(Token);
        var notified = 0;
        f.Manager.HistoryUpdated += _ => notified++;

        Assert.AreEqual(0, await f.Manager.EditTagsAsync([HistoryRecordKey.From(record)], [" kept "], ["missing"], Token));

        f.Db.ChangeTracker.Clear();
        var saved = await f.Db.HistoryRecords.SingleAsync(Token);
        Assert.AreEqual(7, saved.Version);
        Assert.AreEqual(Fixture.OldTime, saved.LastModified);
        Assert.AreEqual(HistorySyncStatus.Synced, saved.SyncStatus);
        Assert.AreEqual(0, notified);
    }

    [TestMethod]
    public async Task BatchEdit_RejectsOverLimitResultWithoutChangingAnyTarget()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var first = f.Add("first", []);
        var full = f.Add("full", Enumerable.Range(0, HistoryTags.MaxCount).Select(i => $"tag{i}").ToArray());
        await f.Db.SaveChangesAsync(Token);
        var notified = 0;
        f.Manager.HistoryUpdated += _ => notified++;

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Manager.EditTagsAsync(
            [HistoryRecordKey.From(first), HistoryRecordKey.From(full)], ["extra"], [], Token));

        // Even a later save must not persist a partial edit left in the tracked entities.
        await f.Db.SaveChangesAsync(Token);
        f.Db.ChangeTracker.Clear();
        var savedFirst = await f.Db.HistoryRecords.SingleAsync(r => r.ID == first.ID, Token);
        var savedFull = await f.Db.HistoryRecords.SingleAsync(r => r.ID == full.ID, Token);
        Assert.IsEmpty(savedFirst.Tags);
        Assert.AreEqual(7, savedFirst.Version);
        Assert.HasCount(HistoryTags.MaxCount, savedFull.Tags);
        Assert.AreEqual(7, savedFull.Version);
        Assert.AreEqual(0, notified);
    }

    [TestMethod]
    public async Task BatchRemoval_AllowsMoreThan32NamesAcrossDifferentRecords()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var names = Enumerable.Range(0, HistoryTags.MaxCount + 1).Select(i => $"tag{i}").ToArray();
        var records = names.Select(name => f.Add(name, [name])).ToArray();
        await f.Db.SaveChangesAsync(Token);

        Assert.AreEqual(records.Length, await f.Manager.EditTagsAsync(
            records.Select(HistoryRecordKey.From).ToArray(), [], names, Token));

        f.Db.ChangeTracker.Clear();
        Assert.IsTrue(await f.Db.HistoryRecords.AllAsync(r => r.Tags.Length == 0, Token));
        Assert.IsEmpty(await f.Manager.GetTagCountsAsync(Token));
    }

    [TestMethod]
    public async Task TagCounts_CountEachActiveRecordOnceAndKeepCaseDistinct()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var first = f.Add("first", ["Work", "Work", "work"]);
        f.Add("second", ["Work"]);
        f.Add("empty", []);
        f.Add("deleted", ["hidden", "Work"]).IsDeleted = true;
        await f.Db.SaveChangesAsync(Token);

        var counts = await f.Manager.GetTagCountsAsync(Token);

        Assert.HasCount(2, counts);
        Assert.AreEqual(2, counts["Work"]);
        Assert.AreEqual(1, counts["work"]);
        Assert.AreEqual(1, await f.Manager.EditTagsAsync([HistoryRecordKey.From(first)], [], ["Work", "work"], Token));
        counts = await f.Manager.GetTagCountsAsync(Token);
        Assert.HasCount(1, counts);
        Assert.AreEqual(1, counts["Work"]);
    }

    [TestMethod]
    public async Task BatchNotifications_PrecedeAnEditStartedBySubscriber()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var first = f.Add("first", []);
        var second = f.Add("second", []);
        await f.Db.SaveChangesAsync(Token);
        var notifications = new List<string>();
        Task<int>? followup = null;
        f.Manager.HistoryUpdated += record =>
        {
            notifications.Add($"{record.Hash}:{record.Version}");
            if (record.ID == first.ID)
                followup = f.Manager.EditTagsAsync([HistoryRecordKey.From(second)], ["later"], [], Token);
        };

        await f.Manager.EditTagsAsync([HistoryRecordKey.From(first), HistoryRecordKey.From(second)], ["initial"], [], Token);
        Assert.IsNotNull(followup);
        await followup.WaitAsync(TimeSpan.FromSeconds(10), Token);

        string[] expected = ["first:8", "second:8", "second:9"];
        CollectionAssert.AreEqual(expected, notifications);
    }

    [TestMethod]
    public async Task DeleteTag_RemovesOnlyExactNameFromAllActiveRecordsAndPreservesContent()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var local = f.Add("local", ["old", "other"]);
        var synced = f.Add("synced", ["old"], HistorySyncStatus.Synced);
        var deleted = f.Add("deleted", ["old"]);
        deleted.IsDeleted = true;
        var differentCase = f.Add("case", ["Old"]);
        local.Stared = true;
        local.Pinned = true;
        await f.Db.SaveChangesAsync(Token);
        var notified = new List<int>();
        f.Manager.HistoryUpdated += record => notified.Add(record.ID);

        await f.Manager.DeleteTagAsync(" old ", Token);

        AssertNames(local.Tags, "other");
        Assert.IsEmpty(synced.Tags);
        AssertNames(deleted.Tags, "old");
        AssertNames(differentCase.Tags, "Old");
        Assert.IsTrue(local.Stared && local.Pinned);
        Assert.IsFalse(local.IsDeleted || synced.IsDeleted);
        Assert.AreEqual(Fixture.OldTime, local.Timestamp);
        Assert.AreEqual(Fixture.OldTime, local.LastAccessed);
        Assert.AreEqual(HistorySyncStatus.LocalOnly, local.SyncStatus);
        Assert.AreEqual(HistorySyncStatus.NeedSync, synced.SyncStatus);
        Assert.AreEqual(8, local.Version);
        Assert.AreEqual(8, synced.Version);
        Assert.AreEqual(7, deleted.Version);
        CollectionAssert.AreEquivalent(new[] { local.ID, synced.ID }, notified);
        Assert.AreEqual(4, await f.Db.HistoryRecords.CountAsync(Token));
    }

    [TestMethod]
    public async Task RenameTag_RequiresConfirmationBeforeMergingAndUpdatesEachSourceRecordOnce()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var source = f.Add("source", ["old", "keep"], HistorySyncStatus.Synced);
        var overlap = f.Add("overlap", ["old", "new"], HistorySyncStatus.Synced);
        var targetOnly = f.Add("target", ["new"]);
        await f.Db.SaveChangesAsync(Token);
        var notified = 0;
        f.Manager.HistoryUpdated += _ => notified++;

        Assert.IsFalse(await f.Manager.RenameTagAsync("old", "new", token: Token));
        await f.Db.SaveChangesAsync(Token);
        AssertNames(source.Tags, "old", "keep");
        Assert.AreEqual(7, source.Version);
        Assert.AreEqual(0, notified);

        Assert.IsTrue(await f.Manager.RenameTagAsync("old", "new", allowMerge: true, token: Token));
        AssertNames(source.Tags, "keep", "new");
        AssertNames(overlap.Tags, "new");
        Assert.AreEqual(8, source.Version);
        Assert.AreEqual(8, overlap.Version);
        Assert.AreEqual(7, targetOnly.Version);
        Assert.AreEqual(HistorySyncStatus.NeedSync, source.SyncStatus);
        Assert.AreEqual(HistorySyncStatus.NeedSync, overlap.SyncStatus);
        Assert.AreEqual(2, notified);
        Assert.IsFalse((await f.Manager.GetTagCountsAsync(Token)).ContainsKey("old"));
    }

    [TestMethod]
    public async Task RenameTag_NormalizesNamesAllowsFullRecordAndSkipsDeletedRecords()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var tags = Enumerable.Range(0, HistoryTags.MaxCount).Select(i => $"tag{i}").ToArray();
        var full = f.Add("full", tags);
        var deleted = f.Add("deleted", ["tag0", "Café"]);
        deleted.IsDeleted = true;
        await f.Db.SaveChangesAsync(Token);

        Assert.IsTrue(await f.Manager.RenameTagAsync("tag0", " Cafe\u0301 ", token: Token));
        Assert.HasCount(HistoryTags.MaxCount, full.Tags);
        Assert.Contains("Café", full.Tags);
        Assert.DoesNotContain("tag0", full.Tags);
        Assert.AreEqual(8, full.Version);
        AssertNames(deleted.Tags, "tag0", "Café");
        Assert.IsTrue(await f.Manager.RenameTagAsync("Café", " Cafe\u0301 ", token: Token));
        Assert.AreEqual(8, full.Version);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Manager.RenameTagAsync("Café", " ", token: Token));
        Assert.Contains("Café", full.Tags);
    }

    private static void AssertNames(IEnumerable<string> actual, params string[] expected) =>
        CollectionAssert.AreEqual(expected, actual.ToArray());

    private sealed class Fixture : IAsyncDisposable
    {
        public static readonly DateTime OldTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public HistoryDbContext Db { get; } = new();
        private readonly SemaphoreSlim semaphore = new(1, 1);
        public HistoryManager Manager { get; } = (HistoryManager)RuntimeHelpers.GetUninitializedObject(typeof(HistoryManager));

        public static async Task<Fixture> CreateAsync(CancellationToken token)
        {
            var fixture = new Fixture();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(HistoryManager).GetField("_dbContext", flags)!.SetValue(fixture.Manager, fixture.Db);
            typeof(HistoryManager).GetField("_dbSemaphore", flags)!.SetValue(fixture.Manager, fixture.semaphore);
            fixture.Db.Database.SetConnectionString("Data Source=:memory:");
            await fixture.Db.Database.OpenConnectionAsync(token);
            await fixture.Db.Database.EnsureCreatedAsync(token);
            return fixture;
        }

        public HistoryRecord Add(string hash, string[] tags, HistorySyncStatus status = HistorySyncStatus.LocalOnly)
        {
            var record = new HistoryRecord
            {
                Type = ProfileType.Text,
                Hash = hash,
                Tags = tags,
                Version = 7,
                LastModified = OldTime,
                LastAccessed = OldTime,
                Timestamp = OldTime,
                SyncStatus = status
            };
            Db.Add(record);
            return record;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            semaphore.Dispose();
        }
    }
}
