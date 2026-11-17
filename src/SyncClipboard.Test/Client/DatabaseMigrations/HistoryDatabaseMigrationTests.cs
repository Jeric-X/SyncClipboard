using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SyncClipboard.Core.Utilities.History;

namespace SyncClipboard.Test.Client.DatabaseMigrations;

[TestClass]
public class HistoryDatabaseMigrationTests
{
    private const string LegacyInsert = """
        INSERT INTO HistoryRecords (Type, Hash, Text, FilePath, Timestamp, Stared, Pinned,
            SyncStatus, LastModified, LastAccessed, Version, IsDeleted, IsLocalFileReady, Size, "From")
        VALUES (1, {0}, 'keep', '["legacy.txt"]', '2026-01-01', 1, 0, 0,
            '2026-01-02', '2026-01-03', 7, 0, 1, 4, 'old-device')
        """;

    private static Task<int> InsertHistoryWithoutTags(HistoryDbContext db, string hash, CancellationToken token) =>
        db.Database.ExecuteSqlRawAsync(LegacyInsert, [hash], token);

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
        await InsertHistoryWithoutTags(db, "legacy", token);

        await db.Database.MigrateAsync(token);
        Assert.IsFalse(db.Database.HasPendingModelChanges());
        var record = await db.HistoryRecords.SingleAsync(token);
        Assert.IsNull(record.Name);
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
        await InsertHistoryWithoutTags(db, "tagged", token);
        await db.Database.ExecuteSqlRawAsync("""UPDATE HistoryRecords SET Tags = '["工作"]' WHERE Hash = 'tagged'""", token);
        await InsertHistoryWithoutTags(db, "legacy", token);

        await db.Database.MigrateAsync(token);
        Assert.IsFalse(db.Database.HasPendingModelChanges());
        var empty = await db.HistoryRecords.SingleAsync(r => r.Hash == "legacy", token);
        Assert.IsNull(empty.Name);
        Assert.IsNotNull(empty.Tags);
        Assert.IsEmpty(empty.Tags);
        var tagged = await db.HistoryRecords.SingleAsync(r => r.Hash == "tagged", token);
        Assert.AreEqual("工作", tagged.Tags!.Single());

        await InsertHistoryWithoutTags(db, "new", token);
        Assert.IsNull((await db.HistoryRecords.SingleAsync(r => r.Hash == "new", token)).Tags);
    }

    [TestMethod]
    public async Task RollbackToBeta1_PreservesUnknownTagsOnFailureAndRestoresLegacyDefaultForKnownTags()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var db = new HistoryDbContext();
        db.Database.SetConnectionString("Data Source=:memory:");
        await db.Database.OpenConnectionAsync(token);
        await db.Database.MigrateAsync(token);
        await InsertHistoryWithoutTags(db, "unknown", token);
        var named = await db.HistoryRecords.SingleAsync(token);
        named.Name = "保留名称";
        await db.SaveChangesAsync(token);

        var migrator = db.GetService<IMigrator>();
        await Assert.ThrowsExactlyAsync<SqliteException>(() => migrator.MigrateAsync("20261027010000_AddHistoryTags", token));
        var restored = await db.HistoryRecords.AsNoTracking().SingleAsync(token);
        Assert.IsNull(restored.Tags);
        Assert.AreEqual(named.Name, restored.Name);
        Assert.IsEmpty(await db.Database.GetPendingMigrationsAsync(token));

        await db.Database.ExecuteSqlRawAsync("""UPDATE HistoryRecords SET Tags = '["工作"]' WHERE Hash = 'unknown'""", token);
        await migrator.MigrateAsync("20261027010000_AddHistoryTags", token);
        await InsertHistoryWithoutTags(db, "beta", token);

        var tags = await db.HistoryRecords.Where(r => r.Hash == "unknown").Select(r => r.Tags).SingleAsync(token);
        Assert.AreEqual("工作", tags!.Single());
        var addedTags = await db.HistoryRecords.Where(r => r.Hash == "beta").Select(r => r.Tags).SingleAsync(token);
        Assert.IsNotNull(addedTags);
        Assert.IsEmpty(addedTags);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("应用图标")]
    public async Task UpgradeFromNullableTags_RoundTripsHistoryName(string? name)
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var db = new HistoryDbContext();
        db.Database.SetConnectionString("Data Source=:memory:");
        await db.Database.OpenConnectionAsync(token);
        await db.GetService<IMigrator>().MigrateAsync("20261116093554_PreserveUnknownHistoryTags", token);
        await InsertHistoryWithoutTags(db, "legacy", token);

        await db.Database.MigrateAsync(token);
        var record = await db.HistoryRecords.SingleAsync(token);
        Assert.IsNull(record.Name);
        record.Name = name;
        await db.SaveChangesAsync(token);
        db.ChangeTracker.Clear();

        var saved = await db.HistoryRecords.SingleAsync(token);
        Assert.AreEqual(name, saved.Name);
        Assert.AreEqual("keep", saved.Text);
        Assert.IsNull(saved.Tags);
    }

    public TestContext TestContext { get; set; } = null!;
}
