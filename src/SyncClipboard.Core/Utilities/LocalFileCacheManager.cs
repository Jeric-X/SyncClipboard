using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using System.Security.Cryptography;
using System.Text.Json;

namespace SyncClipboard.Core.Utilities;

public class LocalFileCacheEntry
{
    public string Id { get; set; } = string.Empty; // 主键
    public string CacheType { get; set; } = string.Empty; // 缓存类型
    public string FilePath { get; set; } = string.Empty; // 缓存文件路径
    public DateTime CreatedTime { get; set; } // 创建时间
    public DateTime LastAccessTime { get; set; } // 最后访问时间
    public string CachedFileHash { get; set; } = string.Empty; // 缓存文件内容hash
    public string Metadata { get; set; } = string.Empty; // 元数据
    public long FileSize { get; set; } // 文件大小
}

public class LocalFileCacheDbContext : DbContext
{
    private readonly string _dbPath;

    public LocalFileCacheDbContext()
    {
        _dbPath = Path.Combine(Env.AppDataFileFolder, "LocalFileCache.db");
    }

    public DbSet<LocalFileCacheEntry> CacheEntries { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LocalFileCacheEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CacheType);
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

public sealed class LocalFileCacheManager : ILocalFileCacheManager, IDisposable
{
    private readonly LocalFileCacheDbContext _dbContext;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public LocalFileCacheManager(ILogger logger)
    {
        _logger = logger;
        _dbContext = new LocalFileCacheDbContext();
        _dbContext.Database.EnsureCreated();
    }

    public async Task<string?> GetCachedFilePathAsync(string cacheType, string id)
    {
        await _semaphore.WaitAsync();
        try
        {
            var entry = await _dbContext.CacheEntries
                .FirstOrDefaultAsync(e => e.Id == id && e.CacheType == cacheType);

            if (entry == null)
                return null;

            if (!File.Exists(entry.FilePath))
            {
                _dbContext.CacheEntries.Remove(entry);
                await _dbContext.SaveChangesAsync();
                return null;
            }

            if (!await IsFileValidAsync(entry))
            {
                _dbContext.CacheEntries.Remove(entry);
                await _dbContext.SaveChangesAsync();
                return null;
            }

            entry.LastAccessTime = DateTime.Now;
            await _dbContext.SaveChangesAsync();

            return entry.FilePath;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SaveCacheEntryAsync(string cacheType, string id, string filePath, object? metadata = null)
    {
        await _semaphore.WaitAsync();
        try
        {
            var fileInfo = new FileInfo(filePath);
            var metadataJson = metadata != null ? JsonSerializer.Serialize(metadata) : string.Empty;


            var entry = await _dbContext.CacheEntries
                .FirstOrDefaultAsync(e => e.Id == id && e.CacheType == cacheType);

            var cachedFileHash = await CalculateFileHashAsync(filePath);
            if (entry == null)
            {
                entry = new LocalFileCacheEntry
                {
                    Id = id,
                    CacheType = cacheType,
                    FilePath = filePath,
                    CachedFileHash = cachedFileHash,
                    Metadata = metadataJson,
                    CreatedTime = DateTime.Now,
                    LastAccessTime = DateTime.Now,
                    FileSize = fileInfo.Length
                };
                _dbContext.CacheEntries.Add(entry);
            }
            else
            {
                if (File.Exists(entry.FilePath) && entry.FilePath != filePath)
                {
                    try
                    {
                        File.Delete(entry.FilePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.Write($"Failed to delete old cache file: {entry.FilePath}, Error: {ex.Message}");
                    }
                }

                entry.FilePath = filePath;
                entry.CachedFileHash = cachedFileHash;
                entry.Metadata = metadataJson;
                entry.LastAccessTime = DateTime.Now;
                entry.FileSize = fileInfo.Length;
            }

            await _dbContext.SaveChangesAsync();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task RemoveCacheEntryAsync(string id)
    {
        await _semaphore.WaitAsync();
        try
        {
            var entries = await _dbContext.CacheEntries
                .Where(e => e.Id == id)
                .ToListAsync();

            foreach (var entry in entries)
            {
                if (File.Exists(entry.FilePath))
                {
                    try
                    {
                        File.Delete(entry.FilePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.Write($"Failed to delete cache file: {entry.FilePath}, Error: {ex.Message}");
                    }
                }

                _dbContext.CacheEntries.Remove(entry);
            }

            if (entries.Count > 0)
            {
                await _dbContext.SaveChangesAsync();
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<int> CleanupOrphanRecordsAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            var allEntries = await _dbContext.CacheEntries.ToListAsync();
            var orphanEntries = new List<LocalFileCacheEntry>();

            // 检查每个数据库记录对应的文件是否存在
            foreach (var entry in allEntries)
            {
                if (!File.Exists(entry.FilePath))
                {
                    orphanEntries.Add(entry);
                }
            }

            // 批量删除孤儿记录
            if (orphanEntries.Count > 0)
            {
                _dbContext.CacheEntries.RemoveRange(orphanEntries);
                await _dbContext.SaveChangesAsync();
            }

            return orphanEntries.Count;
        }
        catch (Exception ex)
        {
            _logger.Write($"Failed to cleanup orphan records: {ex.Message}");
            return 0;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<CacheStats> GetCacheStatsAsync()
    {
        var entries = await _dbContext.CacheEntries.ToListAsync();

        return new CacheStats
        {
            TotalEntries = entries.Count,
            TotalSizeInMB = entries.Sum(e => e.FileSize) / 1024.0 / 1024.0,
            GroupProfileCount = entries.Count(e => e.CacheType == "GroupProfile"),
            GroupProfileSizeInMB = entries.Where(e => e.CacheType == "GroupProfile")
                                         .Sum(e => e.FileSize) / 1024.0 / 1024.0
        };
    }

    private static async Task<bool> IsFileValidAsync(LocalFileCacheEntry entry)
    {
        try
        {
            var fileInfo = new FileInfo(entry.FilePath);
            if (fileInfo.Length != entry.FileSize)
                return false;

            if (!string.IsNullOrEmpty(entry.CachedFileHash))
            {
                var currentHash = await CalculateFileHashAsync(entry.FilePath);
                return currentHash == entry.CachedFileHash;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> CalculateFileHashAsync(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hashBytes = await SHA256.HashDataAsync(stream);
        return Convert.ToBase64String(hashBytes);
    }

    public void Dispose()
    {
        _semaphore?.Dispose();
        _dbContext?.Dispose();
    }
}