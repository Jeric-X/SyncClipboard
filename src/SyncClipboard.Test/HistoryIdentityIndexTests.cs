using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Shared.Profiles;

namespace SyncClipboard.Test;

[TestClass]
public class HistoryIdentityIndexTests
{
    [TestMethod]
    public async Task CaseInsensitiveHashLookup_UsesIdentityIndex()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var db = new HistoryDbContext();
        db.Database.SetConnectionString("Data Source=:memory:");
        await db.Database.OpenConnectionAsync(token);
        await db.Database.EnsureCreatedAsync(token);
        var hash = new string('a', 64);
        db.HistoryRecords.Add(new HistoryRecord { Type = ProfileType.Text, Hash = hash });
        await db.SaveChangesAsync(token);
        db.ChangeTracker.Clear();

        var record = await db.HistoryRecords.SingleAsync(
            row => row.Type == ProfileType.Text && EF.Functions.Like(row.Hash, hash.ToUpperInvariant()), token);
        Assert.AreEqual(hash, record.Hash);

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN SELECT * FROM HistoryRecords WHERE Type = 1 AND Hash LIKE 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'";
        await using var reader = await command.ExecuteReaderAsync(token);
        Assert.IsTrue(await reader.ReadAsync(token));
        var detail = reader.GetString(3);
        Assert.Contains("IX_HistoryRecords_Type_Hash", detail);
        Assert.Contains("Hash>", detail);
    }

    public TestContext TestContext { get; set; } = null!;
}
