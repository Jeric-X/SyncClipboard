namespace SyncClipboard.Core.Utilities.Runner;

using CancelableTask = Func<CancellationToken, Task>;

public class SingletonTask
{
    private CancelableTask? _task;

    private readonly object _lock = new object();
    private CancellationTokenSource _cts = new CancellationTokenSource();

    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    public SingletonTask(CancelableTask task)
    {
        _task = task;
    }

    public SingletonTask()
    {
    }

    public Task Run(CancellationToken? token = null)
    {
        var task = _task ?? throw new InvalidOperationException("Task is not set. Please set a task before running.");
        return Run(task, token);
    }

    public async Task Run(CancelableTask task, CancellationToken? token = null)
    {
        lock (_lock)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = new CancellationTokenSource();
            _task = task;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, token ?? CancellationToken.None);

        await _semaphore.WaitAsync(cts.Token);
        using var scopeGuard = new ScopeGuard(() => _semaphore.Release());

        try
        {
            await _task(cts.Token);
        }
        catch when (_cts.Token.IsCancellationRequested)
        {
        }
    }

    public void Cancel()
    {
        lock (_lock)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = new CancellationTokenSource();
        }
    }

    public void SetTask(CancelableTask? task)
    {
        lock (_lock)
        {
            _task = task;
        }
    }
}
