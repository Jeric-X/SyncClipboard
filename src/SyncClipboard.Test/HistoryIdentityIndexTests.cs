using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Core.Utilities.History;

namespace SyncClipboard.Test;

[TestClass]
public class HistoryIdentityIndexTests
{
    private static readonly string[] expected = ["legacy.txt"];

    [TestMethod]
    public async Task MigrationPreservesRecordsAndIndexesCaseInsensitiveIdentityLookup()
    {
        await using var db = new HistoryDbContext();
        db.Database.SetConnectionString("Data Source=:memory:");
        await db.Database.OpenConnectionAsync(TestContext.CancellationTokenSource.Token);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260901065713_AddHistoryTransferData", TestContext.CancellationTokenSource.Token);
        var hash = new string('a', 64);
        // Seed the historical schema without asking the current model to write columns introduced later.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO HistoryRecords (Type, Hash, Text, FilePath, Timestamp, Stared, Pinned,
                SyncStatus, LastModified, LastAccessed, Version, IsDeleted, IsLocalFileReady, Size, "From")
            VALUES ({(int)ProfileType.Text}, {hash}, 'keep', '["legacy.txt"]', '2026-01-01', 0, 0, 0,
                '2026-01-01', '2026-01-01', 1, 0, 1, 4, '')
            """, TestContext.CancellationTokenSource.Token);
        await migrator.MigrateAsync(cancellationToken: TestContext.CancellationTokenSource.Token);
        db.ChangeTracker.Clear();
        var record = await db.HistoryRecords.SingleAsync(row => row.Type == ProfileType.Text && EF.Functions.Like(row.Hash, hash.ToUpperInvariant()), TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(hash, record.Hash);
        Assert.AreEqual("keep", record.Text);
        CollectionAssert.AreEqual(expected, record.FilePath);
        Assert.IsFalse(db.Database.HasPendingModelChanges());
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN SELECT * FROM HistoryRecords WHERE Type = 1 AND Hash LIKE 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'";
        await using var reader = await command.ExecuteReaderAsync(TestContext.CancellationTokenSource.Token);
        Assert.IsTrue(await reader.ReadAsync(TestContext.CancellationTokenSource.Token));
        var detail = reader.GetString(3);
        Assert.Contains("IX_HistoryRecords_Type_Hash", detail);
        Assert.Contains("Hash>", detail);
    }

    [TestMethod]
    public async Task TagMigrationPreservesExistingRecordsAndPersistsTagChanges()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var db = new HistoryDbContext();
        db.Database.SetConnectionString("Data Source=:memory:");
        await db.Database.OpenConnectionAsync(token);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261010151000_IndexHistoryIdentity", token);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO HistoryRecords (Type, Hash, Text, FilePath, Timestamp, Stared, Pinned,
                SyncStatus, LastModified, LastAccessed, Version, IsDeleted, IsLocalFileReady, Size, "From")
            VALUES (1, 'legacy', 'keep', '["legacy.txt"]', '2026-01-01', 1, 0, 0,
                '2026-01-02', '2026-01-03', 7, 0, 1, 4, 'old-device')
            """, token);

        await db.Database.MigrateAsync(token);
        var record = await db.HistoryRecords.SingleAsync(token);
        Assert.IsEmpty(record.Tags);
        Assert.AreEqual("keep", record.Text);
        CollectionAssert.AreEqual(expected, record.FilePath);
        Assert.IsTrue(record.Stared);
        Assert.AreEqual(7, record.Version);
        Assert.AreEqual("old-device", record.From);
        Assert.AreEqual(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), record.Timestamp);
        Assert.AreEqual(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), record.LastModified);
        Assert.AreEqual(new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), record.LastAccessed);

        string[] tags = ["工作", "Work", "work", "quote\"\\"];
        record.Tags = [.. tags];
        await db.SaveChangesAsync(token);
        db.ChangeTracker.Clear();
        record = await db.HistoryRecords.SingleAsync(token);
        CollectionAssert.AreEqual(tags, record.Tags);

        record.Tags[0] = "修改";
        await db.SaveChangesAsync(token);
        db.ChangeTracker.Clear();
        record = await db.HistoryRecords.SingleAsync(token);
        tags[0] = "修改";
        CollectionAssert.AreEqual(tags, record.Tags);

        record.Tags = [];
        await db.SaveChangesAsync(token);
        db.ChangeTracker.Clear();
        Assert.IsEmpty((await db.HistoryRecords.SingleAsync(token)).Tags);
    }

    public TestContext TestContext { get; set; } = null!;
}
