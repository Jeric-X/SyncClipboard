using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Test;

[TestClass]
public class DelegateExtentionTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void SafeFireAndForget_CatchesSynchronousException()
    {
        Func<Task> action = () => throw new InvalidOperationException("synchronous failure");

        action.SafeFireAndForget();
    }

    [TestMethod]
    public async Task SafeFireAndForget_CatchesAsynchronousExceptionWithoutBlockingCaller()
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedTask = DelegateExtention.SafeFireAndForgetCoreAsync(() => completion.Task);
        Assert.IsFalse(observedTask.IsCompleted);
        completion.SetException(new InvalidOperationException("asynchronous failure"));

        await observedTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationTokenSource.Token);
        Assert.IsTrue(observedTask.IsCompletedSuccessfully);
    }

    [TestMethod]
    public async Task SafeFireAndForget_ObservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await DelegateExtention.SafeFireAndForgetCoreAsync(() => Task.FromCanceled(cancellation.Token));
    }

    [TestMethod]
    public async Task SafeFireAndForget_TimeoutStopsWaitingWithoutCancelingOperation()
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var observedTask = DelegateExtention.SafeFireAndForgetCoreAsync(
                () => completion.Task,
                timeout: TimeSpan.FromMilliseconds(50));

            await observedTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationTokenSource.Token);

            Assert.IsTrue(observedTask.IsCompletedSuccessfully);
            Assert.IsFalse(completion.Task.IsCompleted);
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    [TestMethod]
    public void SafeFireAndForget_ExecutesOperationOnce()
    {
        var invocationCount = 0;
        Func<Task> action = () =>
        {
            invocationCount++;
            return Task.CompletedTask;
        };

        action.SafeFireAndForget();

        Assert.AreEqual(1, invocationCount);
    }

    [TestMethod]
    public async Task SafeFireAndForget_ReturnsBeforeOperationCompletes()
    {
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<Task> action = async () =>
        {
            await release.Task;
            finished.SetResult();
        };

        action.SafeFireAndForget();

        Assert.IsFalse(finished.Task.IsCompleted);
        release.SetResult();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationTokenSource.Token);
    }
}
