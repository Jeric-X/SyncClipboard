
using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Utilities.History;

public class HistoryDbContext : DbContext
{
    private const string DbName = "history.db";
    public DbSet<HistoryRecord> HistoryRecords { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={Path.Combine(Env.AppDataDbPath, DbName)}");
}