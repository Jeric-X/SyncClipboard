using SyncClipboard.Core.Utilities.Runner;

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
}
