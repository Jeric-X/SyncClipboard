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
        scheduler.AddJob<HistoryCleanupJob>(TimeSpan.FromMinutes(1)); // 每30分钟执行一次历史记录清理
        scheduler.AddJob<OrphanedHistoryCleanupJob>(TimeSpan.FromHours(6)); // 每6小时执行一次孤立文件夹清理
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