using Microsoft.Extensions.DependencyInjection;
using Moq;
using Quartz;
using SyncClipboard.Core;
using SyncClipboard.Core.Utilities.Job;
using System.Collections.Concurrent;

namespace SyncClipboard.Test;

[TestClass]
[TestCategory("NonUI")]
public class QuartzCompatibilityTests
{
    public TestContext TestContext { get; set; } = null!;
    private CancellationToken TestCancellation => TestContext.CancellationTokenSource.Token;

    [TestMethod]
    public async Task CoreRegistration_ResolvesOneSchedulerAndDisposesItAsynchronously()
    {
        var collection = new ServiceCollection();
        AppCore.ConfigCommonService(collection);
        collection.AddQuartz();
        Assert.HasCount(1, collection.Where(x => x.ServiceType == typeof(IScheduler) && !x.IsKeyedService));
        var services = collection.BuildServiceProvider();
        IScheduler? scheduler = null;
        try
        {
            var proxy = services.GetRequiredService<IScheduler>();
            var factory = services.GetRequiredService<ISchedulerFactory>();
            scheduler = await factory.GetScheduler(TestCancellation);
            Assert.AreSame(scheduler, await factory.GetScheduler(TestCancellation));
            Assert.AreSame(proxy, services.GetRequiredService<IScheduler>());
            Assert.AreEqual(scheduler.SchedulerInstanceId, proxy.SchedulerInstanceId);
            Assert.AreEqual(SchedulerStatus.Created, scheduler.Status);
            await proxy.Start(TestCancellation);
            Assert.AreEqual(SchedulerStatus.Running, scheduler.Status);
        }
        finally
        {
            await AppCore.DisposeServicesAsync(services);
        }
        Assert.AreEqual(SchedulerStatus.Shutdown, scheduler.Status);
    }

    [TestMethod]
    public async Task ProductRegistration_KeepsSixOriginalIntervalsAndImmediateFirstFiring()
    {
        var scheduler = new RecordingScheduler();
        using var services = scheduler.Services();
        var before = DateTimeOffset.UtcNow;

        await Job.SetUpSchedulerJobs(services, TestCancellation);

        var after = DateTimeOffset.UtcNow;
        Dictionary<Type, TimeSpan> expected = new()
        {
            [typeof(AppdataFileDeleteJob)] = TimeSpan.FromHours(24),
            [typeof(UpdateJob)] = TimeSpan.FromHours(24),
            [typeof(HistoryCleanupJob)] = TimeSpan.FromMinutes(1),
            [typeof(DeletedHistoryDataCleanupJob)] = TimeSpan.FromMinutes(5),
            [typeof(OrphanedHistoryCleanupJob)] = TimeSpan.FromHours(6),
            [typeof(LocalFileCacheCleanupJob)] = TimeSpan.FromHours(6)
        };
        Assert.HasCount(6, scheduler.Jobs);
        foreach (var (detail, trigger) in scheduler.Jobs.Values)
        {
            Assert.IsTrue(expected.Remove(detail.JobType.Type, out var interval));
            Assert.AreEqual(interval, ((ISimpleTrigger)trigger).RepeatInterval);
            Assert.AreEqual(-1, ((ISimpleTrigger)trigger).RepeatCount);
            Assert.IsTrue(trigger.StartTimeUtc >= before && trigger.StartTimeUtc <= after);
            Assert.AreEqual(new TriggerKey(detail.Key.Name, detail.Key.Group), trigger.Key);
        }
        Assert.IsEmpty(expected);
        Assert.AreEqual(1, scheduler.Starts);
    }

    [TestMethod]
    public async Task ConcurrentSetup_AwaitsRegistrationBeforeStartingAndDoesNotDuplicateJobs()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scheduler = new RecordingScheduler { BeforeSchedule = _ => gate.Task };
        using var services = scheduler.Services();
        var first = Job.SetUpSchedulerJobs(services, TestCancellation);
        var second = Job.SetUpSchedulerJobs(services, TestCancellation);
        try
        {
            Assert.IsFalse(first.IsCompleted);
            Assert.IsFalse(second.IsCompleted);
            Assert.AreEqual(0, scheduler.Starts);
            Assert.IsEmpty(scheduler.Jobs);
        }
        finally
        {
            gate.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }
        Assert.HasCount(6, scheduler.Jobs);
        Assert.AreEqual(6, scheduler.Schedules);
    }

    [TestMethod]
    public async Task FailedRegistration_DoesNotStartAndCanResumeWithoutDuplicateJobs()
    {
        var fail = true;
        var scheduler = new RecordingScheduler
        {
            BeforeSchedule = job => fail && job.Key.Name == nameof(UpdateJob)
                ? Task.FromException(new InvalidOperationException("Expected registration failure.")) : Task.CompletedTask
        };
        using var services = scheduler.Services();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Job.SetUpSchedulerJobs(services, TestCancellation));

        Assert.HasCount(1, scheduler.Jobs);
        Assert.AreEqual(0, scheduler.Starts);
        fail = false;
        await Job.SetUpSchedulerJobs(services, TestCancellation);
        Assert.HasCount(6, scheduler.Jobs);
        Assert.AreEqual(6, scheduler.Schedules);
        Assert.AreEqual(1, scheduler.Starts);
    }

    [TestMethod]
    public async Task CanceledSetup_DoesNotRegisterOrStartJobs()
    {
        var scheduler = new RecordingScheduler();
        using var services = scheduler.Services();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => Job.SetUpSchedulerJobs(services, cancellation.Token));

        Assert.IsEmpty(scheduler.Jobs);
        Assert.AreEqual(0, scheduler.Starts);
    }

    [TestMethod]
    public async Task CanceledConcurrentSetup_LeavesTheRunningSetupIntact()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scheduler = new RecordingScheduler { BeforeSchedule = _ => gate.Task };
        using var services = scheduler.Services();
        using var cancellation = new CancellationTokenSource();
        var first = Job.SetUpSchedulerJobs(services, TestCancellation);
        var second = Job.SetUpSchedulerJobs(services, cancellation.Token);
        try
        {
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => second);
            Assert.IsFalse(first.IsCompleted);
            Assert.AreEqual(0, scheduler.Starts);
        }
        finally
        {
            gate.TrySetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }
        Assert.HasCount(6, scheduler.Jobs);
        Assert.AreEqual(6, scheduler.Schedules);
        Assert.AreEqual(1, scheduler.Starts);
    }

    [TestMethod]
    public async Task Setup_AwaitsStartupFailureAndCanRetryWithoutRescheduling()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fail = true;
        var scheduler = new RecordingScheduler { BeforeStart = () => fail ? gate.Task : Task.CompletedTask };
        using var services = scheduler.Services();
        var setup = Job.SetUpSchedulerJobs(services, TestCancellation);
        try
        {
            Assert.HasCount(6, scheduler.Jobs);
            Assert.IsFalse(setup.IsCompleted);
        }
        finally
        {
            gate.TrySetException(new InvalidOperationException("Expected startup failure."));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => setup);
        }
        fail = false;
        await Job.SetUpSchedulerJobs(services, TestCancellation);
        Assert.AreEqual(6, scheduler.Schedules);
        Assert.AreEqual(2, scheduler.Starts);
    }

    [TestMethod]
    public async Task RealScheduler_GracefulShutdownWaitsForTheJobAndRepeatedStartDoesNotDuplicateIt()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new ProbeState();
        state.Execute = async _ =>
        {
            Interlocked.Increment(ref state.Executions);
            started.TrySetResult();
            await release.Task;
        };
        await using var services = ProbeServices(state);
        var scheduler = await services.GetRequiredService<ISchedulerFactory>().GetScheduler(TestCancellation);
        await scheduler.ScheduleJob(JobBuilder.Create<ProbeJob>().Build(), TriggerBuilder.Create().StartNow().Build(), cancellationToken: TestCancellation);
        try
        {
            await scheduler.Start(TestCancellation);
            await scheduler.Start(TestCancellation);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
            var shutdown = scheduler.Shutdown(waitForJobsToComplete: true, cancellationToken: CancellationToken.None).AsTask();
            Assert.IsFalse(shutdown.IsCompleted);
            release.TrySetResult();
            await shutdown.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }
        finally
        {
            release.TrySetResult();
            await scheduler.Shutdown(waitForJobsToComplete: true, cancellationToken: CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }
        Assert.AreEqual(1, state.Executions);
        Assert.AreEqual(1, state.DisposedScopes);
        Assert.AreEqual(SchedulerStatus.Shutdown, scheduler.Status);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RealScheduler_FiresAgainAfterCompletionOrFailureAndDisposesEachScope(bool failFirst)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new ProbeState();
        state.Execute = _ =>
        {
            var count = Interlocked.Increment(ref state.Executions);
            if (count == 1 && failFirst) throw new JobExecutionException("Expected first-firing failure.");
            if (count == 2) completed.TrySetResult();
            return ValueTask.CompletedTask;
        };
        await using var services = ProbeServices(state);
        var scheduler = services.GetRequiredService<IScheduler>();
        var actual = await services.GetRequiredService<ISchedulerFactory>().GetScheduler(TestCancellation);
        await scheduler.ScheduleJob(JobBuilder.Create<ProbeJob>().Build(), TriggerBuilder.Create().StartNow()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMilliseconds(100)).WithRepeatCount(1)).Build(), cancellationToken: TestCancellation);
        try
        {
            await scheduler.Start(TestCancellation);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true, cancellationToken: CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }
        Assert.AreEqual(2, state.Executions);
        Assert.HasCount(2, state.ScopeIds);
        Assert.AreEqual(2, state.ScopeIds.Distinct().Count());
        Assert.AreEqual(2, state.DisposedScopes);
        Assert.AreEqual(SchedulerStatus.Shutdown, actual.Status);
    }

    [TestMethod]
    public async Task RealScheduler_InterruptForwardsCancellationAndAllowsShutdown()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cleanupCancellation = new CancellationTokenSource();
        var state = new ProbeState
        {
            Execute = async token =>
            {
                using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(token, cleanupCancellation.Token);
                started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, linkedCancellation.Token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    canceled.TrySetResult();
                }
            }
        };
        await using var services = ProbeServices(state);
        var scheduler = services.GetRequiredService<IScheduler>();
        var key = new JobKey("cancellation-probe");
        await scheduler.ScheduleJob(JobBuilder.Create<ProbeJob>().WithIdentity(key).Build(), TriggerBuilder.Create().StartNow().Build(), cancellationToken: TestCancellation);
        try
        {
            await scheduler.Start(TestCancellation);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
            Assert.IsTrue(await scheduler.Interrupt(key, TestCancellation));
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(10), TestCancellation);
        }
        finally
        {
            cleanupCancellation.Cancel();
            await scheduler.Interrupt(key, CancellationToken.None);
            await scheduler.Shutdown(waitForJobsToComplete: true, cancellationToken: CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }
        Assert.AreEqual(1, state.DisposedScopes);
    }

    private static ServiceProvider ProbeServices(ProbeState state)
    {
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddScoped<ProbeScope>();
        services.AddQuartz();
        return services.BuildServiceProvider();
    }

    public sealed class ProbeState
    {
        public Func<CancellationToken, ValueTask> Execute { get; set; } = _ => ValueTask.CompletedTask;
        public ConcurrentBag<Guid> ScopeIds { get; } = [];
        public int Executions;
        public int DisposedScopes;
    }

    public sealed class ProbeScope(ProbeState state) : IDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();
        public void Dispose() => Interlocked.Increment(ref state.DisposedScopes);
    }

    public sealed class ProbeJob(ProbeState state, ProbeScope scope) : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Assert.AreEqual(context.CancellationToken, cancellationToken);
            state.ScopeIds.Add(scope.Id);
            return state.Execute(cancellationToken);
        }
    }

    private sealed class RecordingScheduler
    {
        public Dictionary<JobKey, (IJobDetail Detail, ITrigger Trigger)> Jobs { get; } = [];
        public Func<IJobDetail, Task> BeforeSchedule { get; init; } = _ => Task.CompletedTask;
        public Func<Task> BeforeStart { get; init; } = () => Task.CompletedTask;
        public int Starts { get; private set; }
        public int Schedules { get; private set; }
        private readonly Mock<IScheduler> _scheduler = new(MockBehavior.Strict);

        public ServiceProvider Services()
        {
            _scheduler.Setup(x => x.Exists(It.IsAny<JobKey>(), It.IsAny<CancellationToken>()))
                .Returns((JobKey key, CancellationToken _) => new ValueTask<bool>(Jobs.ContainsKey(key)));
            _scheduler.Setup(x => x.ScheduleJob(It.IsAny<IJobDetail>(), It.IsAny<ITrigger>(),
                    It.IsAny<ScheduleJobOptions>(), It.IsAny<CancellationToken>()))
                .Returns((IJobDetail job, ITrigger trigger, ScheduleJobOptions _, CancellationToken token) =>
                    new ValueTask<DateTimeOffset>(Schedule(job, trigger, token)));
            _scheduler.Setup(x => x.Start(It.IsAny<CancellationToken>())).Returns(() =>
            {
                Assert.HasCount(6, Jobs);
                Starts++;
                return new ValueTask(BeforeStart());
            });
            return new ServiceCollection().AddSingleton(_scheduler.Object).BuildServiceProvider();
        }

        private async Task<DateTimeOffset> Schedule(IJobDetail job, ITrigger trigger, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await BeforeSchedule(job);
            Jobs.Add(job.Key, (job, trigger));
            Schedules++;
            return trigger.StartTimeUtc;
        }
    }
}
