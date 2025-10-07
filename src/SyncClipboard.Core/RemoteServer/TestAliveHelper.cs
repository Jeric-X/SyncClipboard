namespace SyncClipboard.Core.RemoteServer;

internal sealed class TestAliveHelper(Func<CancellationToken, Task<bool>> _func) : IDisposable
{
    private CancellationTokenSource? _testAliveCancellationTokenSource;
    private readonly object _testAliveLock = new object();
    private readonly TimeSpan _testAliveInterval = TimeSpan.FromSeconds(10);

    public void Dispose()
    {
        _testAliveCancellationTokenSource?.Cancel();
        _testAliveCancellationTokenSource?.Dispose();
        TestSuccessed = null;
    }

    public event Action? TestSuccessed;

    public void Restart()
    {
        StopTestAliveTask();
        lock (_testAliveLock)
        {
            _testAliveCancellationTokenSource = new CancellationTokenSource();
            _ = RunTestAliveLoopAsync(_testAliveCancellationTokenSource.Token);
        }
    }

    private void StopTestAliveTask()
    {
        lock (_testAliveLock)
        {
            _testAliveCancellationTokenSource?.Cancel();
            _testAliveCancellationTokenSource?.Dispose();
            _testAliveCancellationTokenSource = null;
        }
    }

    private async Task RunTestAliveLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && _func is not null)
            {
                var isAlive = await _func.Invoke(token);
                if (isAlive)
                {
                    TestSuccessed?.Invoke();
                }

                await Task.Delay(_testAliveInterval, token);
            }
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
        }
    }
}
