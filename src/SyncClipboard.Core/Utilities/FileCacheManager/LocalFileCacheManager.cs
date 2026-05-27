using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Interfaces;
using System.Security.Cryptography;
using System.Text.Json;

namespace SyncClipboard.Core.Utilities.FileCacheManager;

public sealed class LocalFileCacheManager : IDisposable
{
    private readonly LocalFileCacheDbContext _dbContext;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private const int SchemaVersion = 1;
    private const string SchemaVersionTableName = "__SchemaVersion";

    public LocalFileCacheManager(ILogger logger)
    {
        _logger = logger;
        EnsureCompatibleDatabase();
        _dbContext = new LocalFileCacheDbContext();
        _dbContext.Database.EnsureCreated();
        WriteSchemaVersion();
    }

    private void EnsureCompatibleDatabase()
    {
        var dbPath = LocalFileCacheDbContext.DbPath;
        if (!File.Exists(dbPath))
            return;

        bool needRecreate = false;
        do
        {
            try
            {
                using var checkContext = new LocalFileCacheDbContext();
                using var connection = checkContext.Database.GetDbConnection();
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{SchemaVersionTableName}'";
                var tableExists = command.ExecuteScalar() != null;

                if (!tableExists)
                {
                    _logger.Write($"LocalFileCache database missing schema version table. Recreating database.");
                    needRecreate = true;
                    break;
                }

                command.CommandText = $"SELECT Version FROM {SchemaVersionTableName} LIMIT 1";
                var storedVersion = Convert.ToInt32(command.ExecuteScalar());

                if (storedVersion != SchemaVersion)
                {
                    _logger.Write($"LocalFileCache database schema version mismatch (expected {SchemaVersion}, found {storedVersion}). Recreating database.");
                    needRecreate = true;
                }
            }
            catch (Exception ex)
            {
                _logger.Write($"Failed to check LocalFileCache database schema: {ex.Message}. Recreating database.");
                needRecreate = true;
            }
        } while (false);

        if (needRecreate)
        {
            LocalFileCacheDbContext.ClearConnectionPool();
            try { File.Delete(dbPath); }
            catch (Exception ex)
            {
                _logger.Write($"Failed to delete database file: {ex.Message}");
                throw;
            }
        }
    }

    private void WriteSchemaVersion()
    {
        try
        {
            var connection = _dbContext.Database.GetDbConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"CREATE TABLE IF NOT EXISTS {SchemaVersionTableName} (Version INTEGER NOT NULL)";
            command.ExecuteNonQuery();
            command.CommandText = $"DELETE FROM {SchemaVersionTableName}";
            command.ExecuteNonQuery();
            command.CommandText = $"INSERT INTO {SchemaVersionTableName} (Version) VALUES ({SchemaVersion})";
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.Write($"Failed to write schema version: {ex.Message}");
        }
    }

    public async Task<string?> GetCachedFilePathAsync(string cacheType, string id, CancellationToken token)
    {
        await _semaphore.WaitAsync(token);
        try
        {
            var entry = await _dbContext.CacheEntries
                .FirstOrDefaultAsync(e => e.Id == id && e.CacheType == cacheType, token);

            if (entry == null)
                return null;

            if (!File.Exists(entry.FilePath))
            {
                _dbContext.CacheEntries.Remove(entry);
                await _dbContext.SaveChangesAsync(token);
                return null;
            }

            if (!await IsFileValidAsync(entry, token))
            {
                _dbContext.CacheEntries.Remove(entry);
                await _dbContext.SaveChangesAsync(token);
                return null;
            }

            entry.LastAccessTime = DateTime.Now;
            await _dbContext.SaveChangesAsync(token);

            return entry.FilePath;
        }
        catch when (!token.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public Task SaveCacheEntryAsync(string cacheType, string id, string filePath, CancellationToken token)
    {
        return SaveCacheEntryAsync(cacheType, id, filePath, null, token);
    }

    public async Task SaveCacheEntryAsync(string cacheType, string id, string filePath, object? metadata = null, CancellationToken token = default)
    {
        await _semaphore.WaitAsync(token);
        try
        {
            var fileInfo = new FileInfo(filePath);
            var metadataJson = metadata != null ? JsonSerializer.Serialize(metadata) : string.Empty;

            var entry = await _dbContext.CacheEntries
                .FirstOrDefaultAsync(e => e.Id == id && e.CacheType == cacheType, token);

            var cachedFileHash = await CalculateFileHashAsync(filePath, token);
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

            await _dbContext.SaveChangesAsync(token);
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

    private static async Task<bool> IsFileValidAsync(LocalFileCacheEntry entry, CancellationToken token)
    {
        try
        {
            var fileInfo = new FileInfo(entry.FilePath);
            if (fileInfo.Length != entry.FileSize)
                return false;

            if (!string.IsNullOrEmpty(entry.CachedFileHash))
            {
                var currentHash = await CalculateFileHashAsync(entry.FilePath, token);
                return currentHash == entry.CachedFileHash;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> CalculateFileHashAsync(string filePath, CancellationToken token)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var hashBytes = await SHA256.HashDataAsync(stream, token);
        return Convert.ToBase64String(hashBytes);
    }

    public void Dispose()
    {
        _semaphore?.Dispose();
        _dbContext?.Dispose();
    }
}