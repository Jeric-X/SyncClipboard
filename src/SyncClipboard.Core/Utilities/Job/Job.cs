using System;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace SyncClipboard.Core.Utilities.Job;

public static class Job
{
    public static void SetUpSchedulerJobs(IServiceProvider services)
    {
        var scheduler = services.GetRequiredService<IScheduler>();
        scheduler.AddJob<AppdataFileDeleteJob>(TimeSpan.FromHours(24));
        scheduler.AddJob<UpdateJob>(TimeSpan.FromHours(24));
        scheduler.AddJob<HistoryCleanupJob>(TimeSpan.FromMinutes(1));
        scheduler.AddJob<DeletedHistoryDataCleanupJob>(TimeSpan.FromMinutes(5));
        scheduler.AddJob<OrphanedHistoryCleanupJob>(TimeSpan.FromHours(6));
        scheduler.AddJob<LocalFileCacheCleanupJob>(TimeSpan.FromHours(6));
        scheduler.Start();
    }

    private static void AddJob<T>(this IScheduler scheduler, TimeSpan interval) where T : IJob
    {
        scheduler.ScheduleJob(
            JobBuilder.Create<T>().Build(),
            TriggerBuilder.Create()
                .StartNow()
                .WithSimpleSchedule(x => x.WithInterval(interval).RepeatForever())
                .Build()
        );
    }
}