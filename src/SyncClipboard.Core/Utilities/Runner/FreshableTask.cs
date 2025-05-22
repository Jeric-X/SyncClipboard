namespace SyncClipboard.Core.Utilities.Runner;

public class FreshableTask(Func<CancellationToken, Task> task)
{
    private readonly Func<CancellationToken, Task> _task = task ?? throw new ArgumentNullException(nameof(task));

    private readonly object _lock = new object();
    private CancellationTokenSource _cts = new CancellationTokenSource();

    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    public async Task Run(CancellationToken? token = null)
    {
        lock (_lock)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = new CancellationTokenSource();
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
}
