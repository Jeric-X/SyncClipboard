using Quartz;
using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Core.Utilities.Job;

public class LocalFileCacheCleanupJob : IJob
{
    private readonly ILocalFileCacheManager _cacheManager;
    private readonly ILogger _logger;

    public LocalFileCacheCleanupJob(ILocalFileCacheManager cacheManager, ILogger logger)
    {
        _cacheManager = cacheManager;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            _logger.Write("Starting cache cleanup job...");
            
            var orphanCount = await _cacheManager.CleanupOrphanRecordsAsync();
            
            if (orphanCount > 0)
            {
                _logger.Write($"Cache cleanup completed: Removed {orphanCount} orphan records");
            }
            else
            {
                _logger.Write("Cache cleanup completed: No orphan records found");
            }
        }
        catch (Exception ex)
        {
            _logger.Write($"Cache cleanup job failed: {ex.Message}");
        }
    }
}