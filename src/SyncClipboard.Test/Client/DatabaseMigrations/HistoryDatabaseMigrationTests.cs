using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SyncClipboard.Core.Utilities.History;

namespace SyncClipboard.Test.Client.DatabaseMigrations;

[TestClass]
public class HistoryDatabaseMigrationTests
{
    private static readonly string[] expected = ["legacy.txt"];

    [TestMethod]
    public async Task Migration_20261027010000_AddHistoryTags()
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

        await migrator.MigrateAsync("20261027010000_AddHistoryTags", token);
        Assert.IsFalse(db.Database.HasPendingModelChanges());
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
