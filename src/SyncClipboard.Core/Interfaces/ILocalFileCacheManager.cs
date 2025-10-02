namespace SyncClipboard.Core.Interfaces;

public interface ILocalFileCacheManager
{
    Task<string?> GetCachedFilePathAsync(string cacheType, string id);
    Task SaveCacheEntryAsync(string cacheType, string id, string filePath, object? metadata = null);
    Task RemoveCacheEntryAsync(string id);
    Task<int> CleanupOrphanRecordsAsync();
    Task<CacheStats> GetCacheStatsAsync();
}

public class CacheStats
{
    public int TotalEntries { get; set; }
    public double TotalSizeInMB { get; set; }
    public int GroupProfileCount { get; set; }
    public double GroupProfileSizeInMB { get; set; }
}