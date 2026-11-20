using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Utilities.History;

public class HistoryDbContext : DbContext
{
    private static string ConnectionString { get; } = new SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(Env.AppDataDbPath, "history.db")
    }.ToString();

    public DbSet<HistoryRecord> HistoryRecords { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HistoryRecord>().Property(record => record.Hash).UseCollation("NOCASE");
        modelBuilder.Entity<HistoryRecord>().HasIndex(record => new { record.Type, record.Hash });
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite(ConnectionString);
}
