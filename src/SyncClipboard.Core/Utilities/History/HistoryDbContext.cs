
using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Utilities.History;

public class HistoryDbContext : DbContext
{
    private const string dbName = "history.db";
    public DbSet<HistoryRecord> HistoryRecords { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HistoryRecord>().Property(record => record.Hash).UseCollation("NOCASE");
        modelBuilder.Entity<HistoryRecord>().HasIndex(record => new { record.Type, record.Hash });
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={Path.Combine(Env.AppDataDbPath, dbName)}");
}