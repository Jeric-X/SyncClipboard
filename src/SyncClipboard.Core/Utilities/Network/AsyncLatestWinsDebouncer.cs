namespace SyncClipboard.Core.Utilities.Network;

public sealed class AsyncLatestWinsDebouncer : IDisposable
{
    private readonly object _lock = new();
    private readonly SemaphoreSlim _serialLock = new(1, 1);
    private CancellationTokenSource? _latestCancellation;
    private bool _disposed;

    public Task ScheduleAsync(Func<CancellationToken, Task> action, TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(action);

        CancellationTokenSource cancellation;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _latestCancellation?.Cancel();
            _latestCancellation?.Dispose();
            _latestCancellation = new();
            cancellation = _latestCancellation;
        }

        return RunAsync(action, delay, cancellation.Token);
    }

    public void CancelPending()
    {
        lock (_lock)
        {
            _latestCancellation?.Cancel();
            _latestCancellation?.Dispose();
            _latestCancellation = null;
        }
    }

    private async Task RunAsync(Func<CancellationToken, Task> action, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            await _serialLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await action(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _serialLock.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        CancelPending();
        _serialLock.Dispose();
    }
}
