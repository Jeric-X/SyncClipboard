using Microsoft.Extensions.DependencyInjection;
using Quartz;
using System.Runtime.CompilerServices;

namespace SyncClipboard.Core.Utilities.Job;

public static class Job
{
    private static readonly ConditionalWeakTable<IScheduler, SemaphoreSlim> SetupLocks = [];

    public static async Task SetUpSchedulerJobs(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var scheduler = services.GetRequiredService<IScheduler>();
        var setupLock = SetupLocks.GetValue(scheduler, _ => new SemaphoreSlim(1, 1));
        await setupLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await scheduler.AddJob<AppdataFileDeleteJob>(TimeSpan.FromHours(24), cancellationToken).ConfigureAwait(false);
            await scheduler.AddJob<UpdateJob>(TimeSpan.FromHours(24), cancellationToken).ConfigureAwait(false);
            await scheduler.AddJob<HistoryCleanupJob>(TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
            await scheduler.AddJob<DeletedHistoryDataCleanupJob>(TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
            await scheduler.AddJob<OrphanedHistoryCleanupJob>(TimeSpan.FromHours(6), cancellationToken).ConfigureAwait(false);
            await scheduler.AddJob<LocalFileCacheCleanupJob>(TimeSpan.FromHours(6), cancellationToken).ConfigureAwait(false);
            await scheduler.Start(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            setupLock.Release();
        }
    }

    private static async ValueTask AddJob<T>(this IScheduler scheduler, TimeSpan interval, CancellationToken cancellationToken) where T : IJob
    {
        var key = new JobKey(typeof(T).Name, "SyncClipboard");
        if (await scheduler.Exists(key, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await scheduler.ScheduleJob(
            JobBuilder.Create<T>().WithIdentity(key).Build(),
            TriggerBuilder.Create()
                .WithIdentity(key.Name, key.Group)
                .StartNow()
                .WithSimpleSchedule(x => x.WithInterval(interval).RepeatForever())
                .Build(),
            cancellationToken: cancellationToken
        ).ConfigureAwait(false);
    }
}
