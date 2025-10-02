using Quartz;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities.FileCacheManager;

namespace SyncClipboard.Core.Utilities.Job;

public class LocalFileCacheCleanupJob(LocalFileCacheManager cacheManager, ILogger logger) : IJob
{
    private readonly LocalFileCacheManager _cacheManager = cacheManager;
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