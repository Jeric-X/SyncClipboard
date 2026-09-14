namespace SyncClipboard.Core.Interfaces;

public interface IThreadDispatcher
{
    Task<T> RunOnMainThreadAsync<T>(Func<Task<T>> func);
    Task RunOnMainThreadAsync(Func<Task> func);
    Task RunOnMainThreadAsync(Func<CancellationToken, Task> func, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return RunOnMainThreadAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return func(cancellationToken);
        });
    }

    Task RunOnMainThreadAsync(Action action);
    bool IsMainThread { get; }
}
