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
            await _logger.WriteAsync("Starting cache cleanup job...");

            var orphanCount = await _cacheManager.CleanupOrphanRecordsAsync();
            await _logger.WriteAsync($"Cache cleanup completed: Removed {orphanCount} orphan records");
        }
        catch (Exception ex)
        {
            await _logger.WriteAsync($"Cache cleanup job failed: {ex.Message}");
        }
    }
}