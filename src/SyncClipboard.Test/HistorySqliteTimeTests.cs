using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Models;
using SyncClipboard.Server.Core.Models;
using ClientHistoryContext = SyncClipboard.Core.Utilities.History.HistoryDbContext;
using ServerHistoryContext = SyncClipboard.Server.Core.Utilities.History.HistoryDbContext;

namespace SyncClipboard.Test;

[TestClass]
[TestCategory("NonUI")]
public class HistorySqliteTimeTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(false, "2026-09-13 05:00:00")]
    [DataRow(true, "2026-09-13 05:00:00")]
    [DataRow(false, "2026-09-13 13:00:00+08:00")]
    [DataRow(true, "2026-09-13 13:00:00+08:00")]
    [DataRow(false, "2026-09-13 01:00:00-04:00")]
    [DataRow(true, "2026-09-13 01:00:00-04:00")]
    public async Task PersistedHistoryTimes_PreserveUtcInstantAfterReadAndSave(bool server, string storedTime)
    {
        var token = TestContext.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(token);
        await using DbContext db = server ? new ServerContext(connection) : new ClientContext(connection);
        await db.Database.EnsureCreatedAsync(token);
        if (server)
            db.Add(new HistoryRecordEntity());
        else
            db.Add(new HistoryRecord());
        await db.SaveChangesAsync(token);

        // Existing databases store UTC timestamps without offsets. Also cover explicit offsets,
        // whose DateTimeKind changed in Microsoft.Data.Sqlite 10.
        var timestampColumn = server ? "CreateTime" : "Timestamp";
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE HistoryRecords SET {timestampColumn} = $time, LastModified = $time, LastAccessed = $time";
        command.Parameters.AddWithValue("$time", storedTime);
        await command.ExecuteNonQueryAsync(token);
        db.ChangeTracker.Clear();

        var expected = new DateTime(2026, 9, 13, 5, 0, 0, DateTimeKind.Utc);
        for (var round = 0; round < 2; round++)
        {
            DateTime[] times;
            if (server)
            {
                var record = await db.Set<HistoryRecordEntity>().SingleAsync(token);
                times = [record.CreateTime, record.LastModified, record.LastAccessed];
                // Force timestamps to be written back, then rematerialize them in the next round.
                db.Entry(record).State = EntityState.Modified;
            }
            else
            {
                var record = await db.Set<HistoryRecord>().SingleAsync(token);
                times = [record.Timestamp, record.LastModified, record.LastAccessed];
                db.Entry(record).State = EntityState.Modified;
            }

            foreach (var time in times)
            {
                Assert.AreEqual(expected, time);
                Assert.AreEqual(DateTimeKind.Utc, time.Kind);
            }
            await db.SaveChangesAsync(token);
            db.ChangeTracker.Clear();
        }
    }

    private sealed class ClientContext(SqliteConnection connection) : ClientHistoryContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(connection);
    }

    private sealed class ServerContext(SqliteConnection connection) : ServerHistoryContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(connection);
    }
}
