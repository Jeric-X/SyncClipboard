using SyncClipboard.Core.Utilities.Runner;
using System.Threading.Channels;

namespace SyncClipboard.Test;

[TestClass]
public class SingletonTaskTest
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task RunDisposesLinkedCancellationTokenSourceAfterCompletion()
    {
        CancellationToken capturedToken = default;
        var singletonTask = new SingletonTask(token =>
        {
            capturedToken = token;
            return Task.CompletedTask;
        });

        await singletonTask.Run();

        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = capturedToken.WaitHandle);
    }

    [TestMethod]
    public async Task CancelDoesNotFaultRunningTaskAfterSourceIsDisposed()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var singletonTask = new SingletonTask(async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });

        var runningTask = singletonTask.Run();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationTokenSource.Token);

        singletonTask.Cancel();

        await runningTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationTokenSource.Token);
    }

    [TestMethod]
    public async Task NonReplacingRunSkipsUntilCanceledWaiterHasExited()
    {
        var runner = new SingletonTask();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = runner.Run(_ => release.Task);
        var context = new QueuedSynchronizationContext();
        var originalContext = SynchronizationContext.Current;
        Task waiting;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            waiting = runner.Run(_ => throw new AssertFailedException("Canceled waiter must not run."));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }

        runner.Cancel();
        release.SetResult();
        await running;
        Assert.IsFalse(waiting.IsCompleted);
        var executed = false;
        await runner.Run(_ =>
        {
            executed = true;
            return Task.CompletedTask;
        }, cancelPrevious: false);
        Assert.IsFalse(executed);

        var continuation = await context.Continuations.Reader.ReadAsync(TestContext.CancellationTokenSource.Token)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        continuation();
        await waiting.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        await runner.Run(_ =>
        {
            executed = true;
            return Task.CompletedTask;
        }, cancelPrevious: false);
        Assert.IsTrue(executed);
    }

    [TestMethod]
    public async Task ReplacementCancelsOldTaskAndWaitsForItsExit()
    {
        var runner = new SingletonTask();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<string>();
        CancellationToken oldToken = default;
        var old = runner.Run(async token =>
        {
            oldToken = token;
            await release.Task;
            events.Add("old exited");
        });
        var replacement = runner.Run(async token =>
        {
            events.Add("replacement started");
            await runner.Run(_ => throw new AssertFailedException("Busy non-replacing work must be skipped."), cancelPrevious: false);
            Assert.IsFalse(token.IsCancellationRequested);
        });
        Assert.IsTrue(oldToken.IsCancellationRequested);
        Assert.IsFalse(replacement.IsCompleted);
        release.SetResult();
        await Task.WhenAll(old, replacement).WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        Assert.HasCount(2, events);
        Assert.AreEqual("old exited", events[0]);
        Assert.AreEqual("replacement started", events[1]);
    }

    [TestMethod]
    public async Task CancellationAfterSemaphoreAcquisitionSkipsTaskBody()
    {
        var runner = new SingletonTask();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = runner.Run(_ => release.Task);
        var context = new QueuedSynchronizationContext();
        var originalContext = SynchronizationContext.Current;
        var executed = false;
        Task waiting;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            waiting = runner.Run(_ =>
            {
                executed = true;
                return Task.CompletedTask;
            });
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }

        release.SetResult();
        await running;
        var continuation = await context.Continuations.Reader.ReadAsync(TestContext.CancellationTokenSource.Token)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        runner.Cancel();
        continuation();
        await waiting.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        Assert.IsFalse(executed);
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        public Channel<Action> Continuations { get; } = Channel.CreateUnbounded<Action>();

        public override void Post(SendOrPostCallback callback, object? state) => Continuations.Writer.TryWrite(() => callback(state));
    }
}
