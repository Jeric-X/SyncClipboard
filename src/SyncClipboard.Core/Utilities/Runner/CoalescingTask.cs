namespace SyncClipboard.Core.Utilities.Runner;

/// <summary>
/// Serializes asynchronous work while merging requests received during an active execution.
/// </summary>
public sealed class CoalescingTask<TRequest>(
    Func<TRequest, TRequest, TRequest> merge,
    Func<TRequest, CancellationToken, Task> execute,
    Func<Exception, Task>? onException = null)
{
    private readonly Func<TRequest, TRequest, TRequest> merge = merge;
    private readonly Func<TRequest, CancellationToken, Task> execute = execute;
    private readonly Func<Exception, Task>? onException = onException;
    private readonly object syncRoot = new();
    private TRequest pendingRequest = default!;
    private bool hasPendingRequest;
    private TaskCompletionSource? activeCompletion;

    /// <summary>
    /// Queues a request. Requests received during execution are merged into one subsequent execution.
    /// The cancellation token cancels a newly started execution, or only this caller's wait when an execution is active.
    /// </summary>
    public Task RunAsync(TRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource? completionToStart = null;
        CancellationTokenSource? cancellationToStart = null;
        Task completionTask;
        lock (syncRoot)
        {
            pendingRequest = hasPendingRequest ? merge(pendingRequest, request) : request;
            hasPendingRequest = true;
            if (activeCompletion is null)
            {
                activeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                completionToStart = activeCompletion;
                cancellationToStart = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }

            completionTask = activeCompletion.Task;
        }

        if (completionToStart is not null)
            _ = ProcessAsync(completionToStart, cancellationToStart!);

        return completionTask.WaitAsync(cancellationToken);
    }

    private async Task ProcessAsync(TaskCompletionSource completion, CancellationTokenSource cancellation)
    {
        try
        {
            while (true)
            {
                TRequest request;
                lock (syncRoot)
                {
                    if (!hasPendingRequest)
                    {
                        if (ReferenceEquals(activeCompletion, completion))
                        {
                            activeCompletion = null;
                        }

                        completion.TrySetResult();
                        return;
                    }

                    request = pendingRequest;
                    pendingRequest = default!;
                    hasPendingRequest = false;
                }

                try
                {
                    await execute(request, cancellation.Token);
                }
                catch (Exception ex) when (onException is not null && ex is not OperationCanceledException)
                {
                    await onException(ex);
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            completion.TrySetCanceled(cancellation.Token);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
        finally
        {
            lock (syncRoot)
            {
                if (ReferenceEquals(activeCompletion, completion))
                {
                    activeCompletion = null;
                    hasPendingRequest = false;
                }
            }

            cancellation.Dispose();
        }
    }
}
