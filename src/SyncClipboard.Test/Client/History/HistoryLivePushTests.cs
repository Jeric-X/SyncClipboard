using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Shared.Profiles;
using System.Reflection;
using System.Runtime.CompilerServices;
using ClientHistoryService = SyncClipboard.Core.UserServices.ClipboardService.HistoryService;
using ServerHistoryDbContext = SyncClipboard.Server.Core.Utilities.History.HistoryDbContext;

namespace SyncClipboard.Test.Client.History;

[TestClass]
public class HistoryLivePushTests
{
    public TestContext TestContext { get; set; } = null!;
    private CancellationToken Token => TestContext.CancellationTokenSource.Token;
    private static readonly string[] ExpectedLocalTags = ["local"];
    private static readonly string[] ExpectedNewTags = ["new"];

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ServerUpgrade_PreservesTagsPreviouslyKeptOnlyOnClient(bool livePush)
    {
        await using var fixture = await Fixture.CreateAsync(Token);
        var record = new HistoryRecord
        {
            Type = ProfileType.Text,
            Hash = "UPGRADE",
            Text = "content",
            Version = 3,
            SyncStatus = HistorySyncStatus.Synced
        };
        fixture.Db.Add(record);
        await fixture.Db.SaveChangesAsync(Token);
        await fixture.Manager.EditTagsAsync([HistoryRecordKey.From(record)], ["local"], [], Token);

        // The legacy server accepts the version/time update but ignores the unsupported Tags field.
        var serverRecord = record.ToHistoryRecordDto().ToEntity("user");
        serverRecord.Tags = [];
        await using var serverDb = new ServerHistoryDbContext();
        serverDb.Database.SetConnectionString("Data Source=:memory:");
        await serverDb.Database.OpenConnectionAsync(Token);
        await serverDb.GetService<IMigrator>().MigrateAsync("20260901065719_AddHistoryTransferDataHash", Token);
        serverDb.Add(serverRecord);
        await serverDb.SaveChangesAsync(Token);
        var legacyResponse = HistoryRecordDto.FromEntity(serverRecord);
        legacyResponse.Tags = null;
        await fixture.Manager.SyncRemoteHistoryAsync([legacyResponse], Token);
        fixture.Db.ChangeTracker.Clear();
        var beforeUpgrade = await fixture.Manager.GetSyncSnapshotAsync(HistoryRecordKey.From(record), Token);
        Assert.IsNotNull(beforeUpgrade);
        CollectionAssert.AreEqual(ExpectedLocalTags, beforeUpgrade.Tags);
        Assert.AreEqual(HistorySyncStatus.Synced, beforeUpgrade.SyncStatus);

        await serverDb.Database.MigrateAsync(Token);
        serverDb.ChangeTracker.Clear();
        var migrated = await serverDb.HistoryRecords.SingleAsync(Token);
        var upgradedResponse = HistoryRecordDto.FromEntity(migrated);
        Assert.IsNull(upgradedResponse.Tags);
        if (livePush)
        {
            var service = (ClientHistoryService)RuntimeHelpers.GetUninitializedObject(typeof(ClientHistoryService));
            typeof(ClientHistoryService).GetField("historyManager", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(service, fixture.Manager);
            await service.ApplyRemoteHistoryChangeAsync(upgradedResponse, Token);
        }
        else
        {
            await fixture.Manager.SyncRemoteHistoryAsync([upgradedResponse], Token);
        }

        fixture.Db.ChangeTracker.Clear();
        var afterUpgrade = await fixture.Manager.GetSyncSnapshotAsync(HistoryRecordKey.From(record), Token);
        Assert.IsNotNull(afterUpgrade);
        CollectionAssert.AreEqual(ExpectedLocalTags, afterUpgrade.Tags);

        // An explicit clear after upgrade must remain an empty array across subsequent server starts.
        migrated.Tags = [];
        migrated.Version++;
        await serverDb.SaveChangesAsync(Token);
        await serverDb.Database.MigrateAsync(Token);
        serverDb.ChangeTracker.Clear();
        var cleared = HistoryRecordDto.FromEntity(await serverDb.HistoryRecords.SingleAsync(Token));
        Assert.IsNotNull(cleared.Tags);
        Assert.IsEmpty(cleared.Tags);
        await fixture.Manager.SyncRemoteHistoryAsync([cleared], Token);
        fixture.Db.ChangeTracker.Clear();
        Assert.IsEmpty((await fixture.Manager.GetSyncSnapshotAsync(HistoryRecordKey.From(record), Token))!.Tags);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LivePush_PreservesMissingTagsAndAppliesExplicitClear(bool explicitClear)
    {
        await using var fixture = await Fixture.CreateAsync(Token);
        var record = new HistoryRecord
        {
            Type = ProfileType.Text,
            Hash = "PUSH",
            Text = "content",
            Version = 3,
            SyncStatus = HistorySyncStatus.Synced
        };
        fixture.Db.Add(record);
        await fixture.Db.SaveChangesAsync(Token);
        await fixture.Manager.EditTagsAsync([HistoryRecordKey.From(record)], ["local"], [], Token);
        var update = record.ToHistoryRecordDto();
        update.Tags = explicitClear ? [] : null;
        update.Starred = true;
        update.Version++;
        update.LastModified = update.LastModified.AddSeconds(1);
        var service = (ClientHistoryService)RuntimeHelpers.GetUninitializedObject(typeof(ClientHistoryService));
        typeof(ClientHistoryService).GetField("historyManager", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, fixture.Manager);
        await service.ApplyRemoteHistoryChangeAsync(update, Token);
        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Manager.GetSyncSnapshotAsync(HistoryRecordKey.From(record), Token);
        Assert.IsNotNull(saved);
        CollectionAssert.AreEqual(explicitClear ? [] : ExpectedLocalTags, saved.Tags);
        Assert.IsTrue(saved.Stared);
        Assert.AreEqual(update.Version, saved.Version);
        Assert.AreEqual(HistorySyncStatus.Synced, saved.SyncStatus);
    }

    [TestMethod]
    public async Task LivePush_DelayedOlderMetadataDoesNotOverwritePendingLocalTags()
    {
        await using var fixture = await Fixture.CreateAsync(Token);
        var record = new HistoryRecord
        {
            Type = ProfileType.Text,
            Hash = "DELAYED",
            Text = "content",
            Version = 3,
            SyncStatus = HistorySyncStatus.Synced
        };
        fixture.Db.Add(record);
        await fixture.Db.SaveChangesAsync(Token);
        var delayed = record.ToHistoryRecordDto();
        await fixture.Manager.EditTagsAsync([HistoryRecordKey.From(record)], ["new"], [], Token);
        var version = record.Version;
        var service = (ClientHistoryService)RuntimeHelpers.GetUninitializedObject(typeof(ClientHistoryService));
        typeof(ClientHistoryService).GetField("historyManager", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, fixture.Manager);
        await service.ApplyRemoteHistoryChangeAsync(delayed, Token);
        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Manager.GetSyncSnapshotAsync(HistoryRecordKey.From(record), Token);
        Assert.IsNotNull(saved);
        CollectionAssert.AreEqual(ExpectedNewTags, saved.Tags);
        Assert.AreEqual(version, saved.Version);
        Assert.AreEqual(HistorySyncStatus.NeedSync, saved.SyncStatus);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public HistoryDbContext Db { get; } = new();
        private readonly SemaphoreSlim semaphore = new(1, 1);
        public HistoryManager Manager { get; } = (HistoryManager)RuntimeHelpers.GetUninitializedObject(typeof(HistoryManager));

        public static async Task<Fixture> CreateAsync(CancellationToken token)
        {
            var fixture = new Fixture();
            typeof(HistoryManager).GetField("_dbContext", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(fixture.Manager, fixture.Db);
            typeof(HistoryManager).GetField("_dbSemaphore", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(fixture.Manager, fixture.semaphore);
            fixture.Db.Database.SetConnectionString("Data Source=:memory:");
            await fixture.Db.Database.OpenConnectionAsync(token);
            await fixture.Db.Database.EnsureCreatedAsync(token);
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            semaphore.Dispose();
        }
    }
}
