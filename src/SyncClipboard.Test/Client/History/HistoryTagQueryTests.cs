using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Shared.Profiles;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SyncClipboard.Test.Client.History;

[TestClass]
public class HistoryTagQueryTests
{
    public TestContext TestContext { get; set; } = null!;
    private CancellationToken Token => TestContext.CancellationTokenSource.Token;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AnyTagFilter_AppliesBeforePagingAndSelectionCounts(bool sortByLastAccessed)
    {
        await using var f = await Fixture.CreateAsync(Token);
        for (var i = 0; i < 80; i++)
        {
            var record = f.Add($"record{i}", i % 4 == 0 ? ["A", "B"] : i % 4 == 1 ? ["B"] : ["AB"]);
            record.Timestamp = Fixture.Time.AddMinutes(-i);
            record.LastAccessed = Fixture.Time.AddMinutes(i);
        }
        await f.Db.SaveChangesAsync(Token);
        var expected = Enumerable.Range(0, 80).Where(i => i % 4 < 2);
        if (sortByLastAccessed)
            expected = expected.Reverse();
        var expectedHashes = expected.Select(i => $"record{i}").ToArray();
        var actual = new List<string>();
        DateTime? cursor = null;
        for (var pageNumber = 0; pageNumber < 5; pageNumber++)
        {
            var page = await f.Manager.GetHistoryAsync(["A", "B"], ProfileTypeFilter.All, before: cursor,
                minSize: 10, sortByLastAccessed: sortByLastAccessed, token: Token);
            if (pageNumber == 4)
            {
                Assert.IsEmpty(page);
                break;
            }
            Assert.HasCount(10, page);
            actual.AddRange(page.Select(r => r.Hash));
            cursor = sortByLastAccessed ? page[^1].LastAccessed : page[^1].Timestamp;
        }
        CollectionAssert.AreEqual(expectedHashes, actual);
        var counts = await f.Manager.GetHistorySelectionCountsAsync(["A", "B"], ProfileTypeFilter.All, null, null,
            [new(ProfileType.Text, "record0"), new(ProfileType.Text, "record1"), new(ProfileType.Text, "record2")], token: Token);
        Assert.AreEqual((40, 2), counts);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PageBoundary_ReturnsAllMatchingRecordsWithTheSameTimestamp(bool sortByLastAccessed)
    {
        await using var f = await Fixture.CreateAsync(Token);
        var older = f.Add("older-match", ["A"]);
        older.Timestamp = Fixture.Time.AddMinutes(-1);
        older.LastAccessed = older.Timestamp;
        f.Add("tie-first", ["A"]);
        f.Add("tie-nonmatch", ["AB"]);
        f.Add("tie-second", ["A"]);
        f.Add("tie-third", ["A"]);
        await f.Db.SaveChangesAsync(Token);

        var page = await f.Manager.GetHistoryAsync(["A"], ProfileTypeFilter.All, minSize: 1,
            sortByLastAccessed: sortByLastAccessed, token: Token);
        string[] expected = ["tie-third", "tie-second", "tie-first"];
        CollectionAssert.AreEqual(expected, page.Select(r => r.Hash).ToArray());
        var cursor = sortByLastAccessed ? page[^1].LastAccessed : page[^1].Timestamp;
        var next = await f.Manager.GetHistoryAsync(["A"], ProfileTypeFilter.All, before: cursor, minSize: 1,
            sortByLastAccessed: sortByLastAccessed, token: Token);
        Assert.HasCount(1, next);
        Assert.AreEqual("older-match", next[0].Hash);
    }

    [TestMethod]
    public async Task TagFilter_NormalizesNamesAndMatchesExactlyWithCaseSensitivity()
    {
        await using var f = await Fixture.CreateAsync(Token);
        f.Add("match", ["Café", "Work"]);
        f.Add("wrong-case", ["café", "work"]);
        f.Add("substring", ["Caféteria", "Workplace"]);
        f.Add("deleted", ["Café", "Work"]).IsDeleted = true;
        await f.Db.SaveChangesAsync(Token);

        string[] tags = [" Cafe\u0301 ", "Work", "Work"];
        var records = await f.Manager.GetHistoryAsync(tags, ProfileTypeFilter.All, token: Token);
        Assert.HasCount(1, records);
        Assert.AreEqual("match", records[0].Hash);
        var counts = await f.Manager.GetHistorySelectionCountsAsync(tags, ProfileTypeFilter.All, null, null,
            [new(ProfileType.Text, "match"), new(ProfileType.Text, "wrong-case"), new(ProfileType.Text, "deleted")], token: Token);
        Assert.AreEqual((1, 1), counts);
    }

    [TestMethod]
    public async Task TagFilter_CombinesWithTypeStarredAndSearchCriteria()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var match = f.Add("match", ["A"]);
        match.Stared = true;
        match.Text = "keep text";
        f.Add("not-starred", ["A"]).Text = "keep text";
        f.Add("other-text", ["A"]).Stared = true;
        var image = f.Add("image", ["A"]);
        image.Type = ProfileType.Image;
        image.Stared = true;
        image.Text = "keep text";
        var untagged = f.Add("untagged", []);
        untagged.Stared = true;
        untagged.Text = "keep text";
        await f.Db.SaveChangesAsync(Token);

        var records = await f.Manager.GetHistoryAsync(["A"], ProfileTypeFilter.Text, started: true,
            searchText: "keep", token: Token);
        Assert.HasCount(1, records);
        Assert.AreEqual("match", records[0].Hash);
        var counts = await f.Manager.GetHistorySelectionCountsAsync(["A"], ProfileTypeFilter.Text, true, "keep",
            [HistoryRecordKey.From(match), HistoryRecordKey.From(image), HistoryRecordKey.From(untagged)], token: Token);
        Assert.AreEqual((1, 1), counts);
    }

    [TestMethod]
    public async Task UntaggedFilter_ReturnsAndCountsOnlyLiveRecordsWithoutTags()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var empty = f.Add("empty", []);
        var tagged = f.Add("tagged", ["A"]);
        f.Add("deleted-empty", []).IsDeleted = true;
        await f.Db.SaveChangesAsync(Token);

        var records = await f.Manager.GetHistoryAsync(null, ProfileTypeFilter.All, onlyUntagged: true, token: Token);
        Assert.HasCount(1, records);
        Assert.AreEqual("empty", records[0].Hash);
        var counts = await f.Manager.GetHistorySelectionCountsAsync([], ProfileTypeFilter.All, null, null,
            [HistoryRecordKey.From(empty), HistoryRecordKey.From(tagged)], onlyUntagged: true, token: Token);
        Assert.AreEqual((1, 1), counts);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NoTagFilter_PreservesUnfilteredResultsAndCounts(bool emptyArray)
    {
        await using var f = await Fixture.CreateAsync(Token);
        var empty = f.Add("empty", []);
        var tagged = f.Add("tagged", ["A"]);
        f.Add("deleted", ["A"]).IsDeleted = true;
        await f.Db.SaveChangesAsync(Token);
        string[]? tags = emptyArray ? [] : null;
        var records = await f.Manager.GetHistoryAsync(tags, ProfileTypeFilter.All, token: Token);
        string[] expected = ["empty", "tagged"];
        CollectionAssert.AreEquivalent(expected, records.Select(r => r.Hash).ToArray());
        var counts = await f.Manager.GetHistorySelectionCountsAsync(tags, ProfileTypeFilter.All, null, null,
            [HistoryRecordKey.From(empty), HistoryRecordKey.From(tagged)], token: Token);
        Assert.AreEqual((2, 2), counts);
    }

    [TestMethod]
    [DataRow(" ", false)]
    [DataRow("bad\nname", false)]
    [DataRow("A", true)]
    public async Task InvalidTagFilter_IsRejectedByQueriesAndCounts(string tag, bool onlyUntagged)
    {
        await using var f = await Fixture.CreateAsync(Token);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Manager.GetHistoryAsync(
            [tag], ProfileTypeFilter.All, onlyUntagged: onlyUntagged, token: Token));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Manager.GetHistorySelectionCountsAsync(
            [tag], ProfileTypeFilter.All, null, null, [], onlyUntagged: onlyUntagged, token: Token));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public static readonly DateTime Time = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public HistoryDbContext Db { get; } = new();
        private readonly SemaphoreSlim semaphore = new(1, 1);
        public HistoryManager Manager { get; } = (HistoryManager)RuntimeHelpers.GetUninitializedObject(typeof(HistoryManager));

        public static async Task<Fixture> CreateAsync(CancellationToken token)
        {
            var f = new Fixture();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(HistoryManager).GetField("_dbContext", flags)!.SetValue(f.Manager, f.Db);
            typeof(HistoryManager).GetField("_dbSemaphore", flags)!.SetValue(f.Manager, f.semaphore);
            f.Db.Database.SetConnectionString("Data Source=:memory:");
            await f.Db.Database.OpenConnectionAsync(token);
            await f.Db.Database.EnsureCreatedAsync(token);
            return f;
        }

        public HistoryRecord Add(string hash, string[] tags)
        {
            var record = new HistoryRecord
            {
                Type = ProfileType.Text,
                Hash = hash,
                Tags = tags,
                Timestamp = Time,
                LastAccessed = Time
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
