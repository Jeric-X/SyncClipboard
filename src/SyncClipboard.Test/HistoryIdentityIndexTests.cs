using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SyncClipboard.Core.Models;
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
        db.HistoryRecords.Add(new HistoryRecord { Type = ProfileType.Text, Hash = hash, Text = "keep", FilePath = ["legacy.txt"] });
        await db.SaveChangesAsync(TestContext.CancellationTokenSource.Token);
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

    public TestContext TestContext { get; set; } = null!;
}
