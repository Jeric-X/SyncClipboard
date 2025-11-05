
using Microsoft.EntityFrameworkCore;
using SyncClipboard.Server.Core.Models;

namespace SyncClipboard.Server.Core.Utilities.History;

public class HistoryDbContext : DbContext
{
    private readonly string _dbPath;

    public HistoryDbContext()
    {
        // Default constructor for design-time services like migrations
        _dbPath = "history.db";
    }

    public HistoryDbContext(DbContextOptions<HistoryDbContext> options, IWebHostEnvironment env) : base(options)
    {
        var webRoot = string.IsNullOrEmpty(env.WebRootPath) ? env.ContentRootPath : env.WebRootPath;
        var dataFolder = Path.Combine(webRoot, "data");
        if (!Directory.Exists(dataFolder)) Directory.CreateDirectory(dataFolder);
        _dbPath = Path.Combine(dataFolder, "history.db");
    }

    public DbSet<HistoryRecordEntity> HistoryRecords { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
        {
            options.UseSqlite($"Data Source={_dbPath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HistoryRecordEntity>()
            .HasIndex(e => new { e.UserId, e.CreateTime, e.ID })
            .HasDatabaseName("IX_History_User_CreateTime_ID");

        // Optimize favorite (starred) queries combined with time-range and paging
        modelBuilder.Entity<HistoryRecordEntity>()
            .HasIndex(e => new { e.UserId, e.Stared, e.CreateTime, e.ID })
            .HasDatabaseName("IX_History_User_Stared_CreateTime_ID");

        // Further optimize when filtering by both starred and type
        modelBuilder.Entity<HistoryRecordEntity>()
            .HasIndex(e => new { e.UserId, e.Stared, e.Type, e.CreateTime, e.ID })
            .HasDatabaseName("IX_History_User_Stared_Type_CreateTime_ID");

        base.OnModelCreating(modelBuilder);
    }
}
