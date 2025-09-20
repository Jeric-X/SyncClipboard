namespace SyncClipboard.Core.Interfaces;

public interface ILocalFileCacheManager
{
    // 核心缓存操作
    Task<string?> GetCachedFilePathAsync(string cacheType, string id);
    Task SaveCacheEntryAsync(string cacheType, string id, string filePath, object? metadata = null);
    Task RemoveCacheEntryAsync(string id);
    
    // 孤儿记录清理
    Task<int> CleanupOrphanRecordsAsync();
    
    // 统计信息
    Task<CacheStats> GetCacheStatsAsync();
}

public class CacheStats
{
    public int TotalEntries { get; set; }
    public double TotalSizeInMB { get; set; }
    public int GroupProfileCount { get; set; }
    public double GroupProfileSizeInMB { get; set; }
}