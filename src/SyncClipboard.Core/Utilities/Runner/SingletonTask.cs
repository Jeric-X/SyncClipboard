namespace SyncClipboard.Core.Utilities.Runner;

using CancelableTask = Func<CancellationToken, Task>;

public class SingletonTask
{
    private CancelableTask? _task;

    private CancellationTokenSource _cts = new CancellationTokenSource();
    private readonly object _ctsLock = new object();
    private readonly SemaphoreSlim _taskSemaphore = new SemaphoreSlim(1, 1);

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

    public Task Run(CancelableTask task, CancellationToken? token = null)
    {
        return Run(task, cancelPrevious: true, token);
    }

    public async Task Run(CancelableTask task, bool cancelPrevious, CancellationToken? token = null)
    {
        CancellationTokenSource methodLevelCts;
        CancellationTokenSource linkedCts;

        lock (_ctsLock)
        {
            if (!cancelPrevious && _taskSemaphore.CurrentCount == 0)
            {
                return;
            }

            _cts.Cancel();
            _cts = new CancellationTokenSource();
            methodLevelCts = _cts;
            _task = task;
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, token ?? CancellationToken.None);
        }

        try
        {
            await _taskSemaphore.WaitAsync(linkedCts.Token);
            using var scopeGuard = new ScopeGuard(() => _taskSemaphore.Release());
            await task(linkedCts.Token);
        }
        catch when (methodLevelCts.Token.IsCancellationRequested)
        {
            methodLevelCts.Dispose();
        }
    }

    public void Cancel()
    {
        lock (_ctsLock)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = new CancellationTokenSource();
        }
    }

    public void SetTask(CancelableTask? task)
    {
        lock (_ctsLock)
        {
            _task = task;
        }
    }
}
