using Quartz;
using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Core.Utilities.Job;

public class LocalFileCacheCleanupJob(ILocalFileCacheManager cacheManager, ILogger logger) : IJob
{
    private readonly ILocalFileCacheManager _cacheManager = cacheManager;
    private readonly ILogger _logger = logger;

    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            _logger.Write("Starting cache cleanup job...");

            var orphanCount = await _cacheManager.CleanupOrphanRecordsAsync();
            _logger.Write($"Cache cleanup completed: Removed {orphanCount} orphan records");
        }
        catch (Exception ex)
        {
            _logger.Write($"Cache cleanup job failed: {ex.Message}");
        }
    }
}