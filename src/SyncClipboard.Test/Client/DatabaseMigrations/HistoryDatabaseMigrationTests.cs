using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.History;

namespace SyncClipboard.Test.Client.DatabaseMigrations;

[TestClass]
public class HistoryDatabaseMigrationTests
{
    private const string LegacyInsert = """
        INSERT INTO HistoryRecords (Type, Hash, Text, FilePath, Timestamp, Stared, Pinned,
            SyncStatus, LastModified, LastAccessed, Version, IsDeleted, IsLocalFileReady, Size, "From")
        VALUES (1, 'legacy', 'keep', '["legacy.txt"]', '2026-01-01', 1, 0, 0,
            '2026-01-02', '2026-01-03', 7, 0, 1, 4, 'old-device')
        """;

    private static readonly string[] expected = ["legacy.txt"];

    [TestMethod]
    public async Task UpgradeFrom333_PreservesHistoryWithUnknownTags()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var db = new HistoryDbContext();
        db.Database.SetConnectionString("Data Source=:memory:");
        await db.Database.OpenConnectionAsync(token);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261010151000_IndexHistoryIdentity", token);
        await db.Database.ExecuteSqlRawAsync(LegacyInsert, token);

        await db.Database.MigrateAsync(token);
        Assert.IsFalse(db.Database.HasPendingModelChanges());
        var record = await db.HistoryRecords.SingleAsync(token);
        Assert.IsNull(record.Tags);
        Assert.AreEqual("legacy", record.Hash);
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

        Assert.IsNotNull(record.Tags);
        record.Tags[0] = "修改";
        await db.SaveChangesAsync(token);
        db.ChangeTracker.Clear();
        record = await db.HistoryRecords.SingleAsync(token);
        tags[0] = "修改";
        CollectionAssert.AreEqual(tags, record.Tags);

        record.Tags = [];
        await db.SaveChangesAsync(token);
        db.ChangeTracker.Clear();
        var cleared = await db.HistoryRecords.SingleAsync(token);
        Assert.IsNotNull(cleared.Tags);
        Assert.IsEmpty(cleared.Tags);
    }

    [TestMethod]
    public async Task UpgradeFromBeta1_PreservesStoredTags()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var db = new HistoryDbContext();
        db.Database.SetConnectionString("Data Source=:memory:");
        await db.Database.OpenConnectionAsync(token);
        await db.GetService<IMigrator>().MigrateAsync("20261010151000_IndexHistoryIdentity", token);
        // Recreate the released beta1 schema and migration history, including its non-null Tags default.
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE HistoryRecords ADD COLUMN Tags TEXT NOT NULL DEFAULT '[]';
            INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
                VALUES ('20261027010000_AddHistoryTags', '9.0.20');
            """, token);
        await db.Database.ExecuteSqlRawAsync(LegacyInsert, token);
        await db.Database.ExecuteSqlRawAsync("""UPDATE HistoryRecords SET Hash = 'tagged', Tags = '["工作"]'""", token);
        await db.Database.ExecuteSqlRawAsync(LegacyInsert, token);

        await db.Database.MigrateAsync(token);
        Assert.IsFalse(db.Database.HasPendingModelChanges());
        var empty = await db.HistoryRecords.SingleAsync(r => r.Hash == "legacy", token);
        Assert.IsNotNull(empty.Tags);
        Assert.IsEmpty(empty.Tags);
        var tagged = await db.HistoryRecords.SingleAsync(r => r.Hash == "tagged", token);
        Assert.AreEqual("工作", tagged.Tags!.Single());

        db.HistoryRecords.Add(new HistoryRecord { Hash = "new" });
        await db.SaveChangesAsync(token);
        db.ChangeTracker.Clear();
        Assert.IsNull((await db.HistoryRecords.SingleAsync(r => r.Hash == "new", token)).Tags);
    }

    [TestMethod]
    public async Task RollbackToBeta1_PreservesTagEditsOnNextUpgrade()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var db = new HistoryDbContext();
        db.Database.SetConnectionString("Data Source=:memory:");
        await db.Database.OpenConnectionAsync(token);
        await db.Database.MigrateAsync(token);
        await db.Database.ExecuteSqlRawAsync(LegacyInsert, token);
        await db.Database.ExecuteSqlRawAsync("UPDATE HistoryRecords SET Hash = 'unknown'", token);
        await db.Database.ExecuteSqlRawAsync(LegacyInsert, token);
        await db.Database.ExecuteSqlRawAsync("UPDATE HistoryRecords SET Hash = 'edited' WHERE Hash = 'legacy'", token);
        await db.GetService<IMigrator>().MigrateAsync("20261027010000_AddHistoryTags", token);
        await db.Database.ExecuteSqlRawAsync("UPDATE HistoryRecords SET Stared = 0, Version = Version + 1", token);
        await db.Database.ExecuteSqlRawAsync("""UPDATE HistoryRecords SET Tags = '["工作"]' WHERE Hash = 'edited'""", token);
        await db.Database.ExecuteSqlRawAsync("UPDATE HistoryRecords SET Tags = '[]' WHERE Hash = 'edited'", token);

        await db.Database.ExecuteSqlRawAsync(LegacyInsert, token);
        await db.Database.MigrateAsync(token);
        var unknown = await db.HistoryRecords.SingleAsync(r => r.Hash == "unknown", token);
        Assert.IsNull(unknown.Tags);
        Assert.IsFalse(unknown.Stared);
        var edited = await db.HistoryRecords.SingleAsync(r => r.Hash == "edited", token);
        Assert.IsNotNull(edited.Tags);
        Assert.IsEmpty(edited.Tags);
        var record = await db.HistoryRecords.SingleAsync(r => r.Hash == "legacy", token);
        Assert.IsNotNull(record.Tags);
        Assert.IsEmpty(record.Tags);
    }

    public TestContext TestContext { get; set; } = null!;
}
