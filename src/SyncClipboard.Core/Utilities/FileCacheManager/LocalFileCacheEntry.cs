namespace SyncClipboard.Core.Utilities.FileCacheManager;

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