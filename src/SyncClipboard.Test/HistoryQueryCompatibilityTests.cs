using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Shared.Profiles;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SyncClipboard.Test;

[TestClass]
public class HistoryQueryCompatibilityTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(ProfileTypeFilter.Text, 1)]
    [DataRow(ProfileTypeFilter.Image, 1)]
    [DataRow(ProfileTypeFilter.FileAndGroup, 2)]
    [DataRow(ProfileTypeFilter.All, 4)]
    [DataRow(ProfileTypeFilter.None, 0)]
    public async Task CategoryQueries_ReturnMatchingRecordsAndCounts(ProfileTypeFilter filter, int expectedCount)
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        HistoryRecordKey[] selected = [new(ProfileType.Text, "text"), new(ProfileType.Image, "image")];

        var records = await fixture.Manager.GetHistoryAsync(filter, token: TestContext.CancellationTokenSource.Token);
        var (totalCount, selectedCount) = await fixture.Manager.GetHistorySelectionCountsAsync(
            filter, null, null, selected, TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(expectedCount, records.Count);
        Assert.AreEqual(expectedCount, totalCount);
        Assert.IsTrue(records.All(record => ((int)filter & (1 << (int)record.Type)) != 0));
        Assert.AreEqual(records.Count(record => selected.AsEnumerable().Contains(HistoryRecordKey.From(record))),
            selectedCount);
    }

    [TestMethod]
    public async Task CategoryQuery_PreservesSearchStarredAndPaginationFilters()
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        var record = await fixture.Db.HistoryRecords.SingleAsync(record => record.Hash == "text", TestContext.CancellationTokenSource.Token);
        record.Stared = true;
        await fixture.Db.SaveChangesAsync(TestContext.CancellationTokenSource.Token);

        var records = await fixture.Manager.GetHistoryAsync(ProfileTypeFilter.Text, started: true,
            before: record.Timestamp.AddSeconds(1), minSize: 1, searchText: "keep", token: TestContext.CancellationTokenSource.Token);
        var counts = await fixture.Manager.GetHistorySelectionCountsAsync(ProfileTypeFilter.Text, true, "keep",
            [HistoryRecordKey.From(record)], TestContext.CancellationTokenSource.Token);

        Assert.HasCount(1, records);
        Assert.AreEqual("text", records[0].Hash);
        Assert.AreEqual((1, 1), counts);
        Assert.IsEmpty(await fixture.Manager.GetHistoryAsync(ProfileTypeFilter.Text,
            before: record.Timestamp, minSize: 1, token: TestContext.CancellationTokenSource.Token));
    }

    [TestMethod]
    public async Task SetStarred_UpdatesOnlyExactLiveCompositeKeys()
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        fixture.Db.HistoryRecords.Add(new HistoryRecord { Type = ProfileType.Image, Hash = "text" });
        await fixture.Db.SaveChangesAsync(TestContext.CancellationTokenSource.Token);

        await fixture.Manager.SetStarredAsync([
            new(ProfileType.Text, "text"), new(ProfileType.Image, "image"), new(ProfileType.Text, "deleted")], true, TestContext.CancellationTokenSource.Token);

        var starred = await fixture.Db.HistoryRecords.Where(record => record.Stared).ToListAsync(TestContext.CancellationTokenSource.Token);
        Assert.HasCount(2, starred);
        Assert.IsTrue(starred.Any(record => record.Type == ProfileType.Text && record.Hash == "text"));
        Assert.IsTrue(starred.Any(record => record.Type == ProfileType.Image && record.Hash == "image"));
    }

    private sealed class MemoryHistoryDbContext : HistoryDbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite("Data Source=:memory:");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public MemoryHistoryDbContext Db { get; } = new();
        public HistoryManager Manager { get; }
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        private Fixture()
        {
            // Bypass application startup/configuration and the user's on-disk database; exercise the real query methods.
            Manager = (HistoryManager)RuntimeHelpers.GetUninitializedObject(typeof(HistoryManager));
            typeof(HistoryManager).GetField("_dbContext", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Manager, Db);
            typeof(HistoryManager).GetField("_dbSemaphore", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Manager, _semaphore);
        }

        public static async Task<Fixture> CreateAsync(CancellationToken token)
        {
            var fixture = new Fixture();
            await fixture.Db.Database.OpenConnectionAsync(token);
            await fixture.Db.Database.EnsureCreatedAsync(token);
            fixture.Db.HistoryRecords.AddRange(
                new HistoryRecord { Type = ProfileType.Text, Hash = "text", Text = "keep text" },
                new HistoryRecord { Type = ProfileType.Image, Hash = "image" },
                new HistoryRecord { Type = ProfileType.File, Hash = "file" },
                new HistoryRecord { Type = ProfileType.Group, Hash = "group" },
                new HistoryRecord { Type = ProfileType.Text, Hash = "deleted", Text = "keep deleted", IsDeleted = true });
            await fixture.Db.SaveChangesAsync(token);
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            _semaphore.Dispose();
        }
    }
}
