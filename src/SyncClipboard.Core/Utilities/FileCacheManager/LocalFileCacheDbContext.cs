using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Commons;

namespace SyncClipboard.Core.Utilities.FileCacheManager;

public class LocalFileCacheDbContext : DbContext
{
    public static string DbPath { get; } = Path.Combine(Env.AppDataFileFolder, "LocalFileCache.db");

    public DbSet<LocalFileCacheEntry> CacheEntries { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={DbPath}");
    }

    public static void ClearConnectionPool()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DbPath}");
        Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LocalFileCacheEntry>(entity =>
        {
            entity.HasKey(e => new { e.Id, e.CacheType });
            entity.HasIndex(e => e.LastAccessTime);
            entity.HasIndex(e => e.CachedFileHash);
            entity.HasIndex(e => new { e.CacheType, e.CachedFileHash });

            entity.Property(e => e.Id).HasMaxLength(64);
            entity.Property(e => e.CacheType).HasMaxLength(50);
            entity.Property(e => e.FilePath).HasMaxLength(500);
            entity.Property(e => e.CachedFileHash).HasMaxLength(64);
        });
    }
}