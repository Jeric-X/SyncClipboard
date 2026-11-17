using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Utilities.History;
using SyncClipboard.Shared.Profiles;

namespace SyncClipboard.Test.Server.History;

[TestClass]
public class ServerHistoryNameMigrationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Upgrade_PreservesExistingHistoryWithUnknownName()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var db = new HistoryDbContext();
        db.Database.SetConnectionString("Data Source=:memory:");
        await db.Database.OpenConnectionAsync(token);
        await db.GetService<IMigrator>().MigrateAsync("20261110024320_InitializeLegacyHistoryTags", token);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO HistoryRecords (UserId, Type, Hash, Text, Size, CreateTime, LastAccessed, LastModified, Version,
                Stared, Pinned, IsDeleted, TransferDataFile, TransferDataSha256, TransferDataMd5, FilePaths, "From", Tags)
            VALUES ('user', 1, 'ABC', 'icon.png', 42, '2026-10-01', '2026-10-02', '2026-10-03', 7,
                1, 0, 0, 'icon.png', '', '', '["icon.png"]', 'desktop', '["work"]')
            """, token);

        await db.Database.MigrateAsync(token);

        var record = await db.HistoryRecords.SingleAsync(token);
        Assert.IsNull(record.Name);
        Assert.AreEqual("user", record.UserId);
        Assert.AreEqual(ProfileType.File, record.Type);
        Assert.AreEqual("ABC", record.Hash);
        Assert.AreEqual("icon.png", record.Text);
        Assert.AreEqual("icon.png", record.FilePaths.Single());
        Assert.AreEqual("work", record.Tags!.Single());
        Assert.IsTrue(record.Stared);
        Assert.AreEqual(7, record.Version);
        Assert.AreEqual(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), record.CreateTime);
        Assert.AreEqual(new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), record.LastAccessed);
        Assert.AreEqual(new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc), record.LastModified);
    }

    [TestMethod]
    public async Task FreshDatabase_RoundTripsUnknownSetAndClearedName()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var db = new HistoryDbContext();
        db.Database.SetConnectionString("Data Source=:memory:");
        await db.Database.OpenConnectionAsync(token);
        await db.Database.MigrateAsync(token);
        Assert.IsFalse(db.Database.HasPendingModelChanges());

        var record = new HistoryRecordEntity { Text = "icon.png" };
        db.HistoryRecords.Add(record);
        await db.SaveChangesAsync(token);
        db.ChangeTracker.Clear();
        record = await db.HistoryRecords.SingleAsync(token);
        Assert.IsNull(record.Name);

        foreach (var name in new[] { "应用图标", string.Empty })
        {
            record.Name = name;
            await db.SaveChangesAsync(token);
            db.ChangeTracker.Clear();
            record = await db.HistoryRecords.SingleAsync(token);
            Assert.AreEqual(name, record.Name);
            Assert.AreEqual("icon.png", record.Text);
        }
    }
}
