namespace SyncClipboard.Core.Utilities.Images;

/// <summary>Changes the admission limit without interrupting native decodes already in progress.</summary>
public sealed class PreviewImageLoadLimiter
{
    private readonly Lock _gate = new();
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _limit;
    private int _active;

    public PreviewImageLoadLimiter(int limit = 1) => SetLimit(limit);

    public void SetLimit(int limit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        lock (_gate)
        {
            _limit = limit;
            Pulse();
        }
    }

    public async Task<IDisposable> AcquireAsync(CancellationToken token)
    {
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                if (_active < _limit)
                {
                    _active++;
                    return new Permit(this);
                }
                changed = _changed.Task;
            }
            await changed.WaitAsync(token).ConfigureAwait(false);
        }
    }

    private void Release()
    {
        lock (_gate)
        {
            _active--;
            Pulse();
        }
    }

    private void Pulse()
    {
        var previous = _changed;
        _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        previous.TrySetResult();
    }

    private sealed class Permit(PreviewImageLoadLimiter owner) : IDisposable
    {
        private PreviewImageLoadLimiter? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}
