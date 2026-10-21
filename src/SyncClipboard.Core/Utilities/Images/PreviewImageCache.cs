using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Utilities.Images;

/// <summary>
/// Shares decoding and accounts for both active and idle images. Only idle entries may be evicted.
/// The loader runs on the first caller's context, allowing platform backends to use their UI thread.
/// </summary>
public sealed class PreviewImageCache<TKey, T> : IDisposable where TKey : notnull where T : class
{
    private readonly Lock _gate = new();
    private readonly Dictionary<TKey, Entry> _entries = [];
    private readonly LinkedList<Entry> _idle = [];
    private readonly Func<TKey, CancellationToken, Task<T>> _load;
    private readonly Func<T, long> _measure;
    private readonly Action<T> _release;
    private long _capacity;
    private long _bytes;
    private bool _disposed;

    private sealed class Entry(TKey key)
    {
        public TKey Key { get; } = key;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenSource Cancellation { get; } = new();
        public T? Image;
        public long Bytes;
        public int Users;
        public bool Invalid;
        public bool Counted;
        public LinkedListNode<Entry>? IdleNode;
    }

    public PreviewImageCache(long capacity, Func<TKey, CancellationToken, Task<T>> load,
        Func<T, long> measure, Action<T> release)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _capacity = capacity;
        _load = load;
        _measure = measure;
        _release = release;
    }

    public void FollowPerformanceConfiguration(ConfigManager config) =>
        config.GetAndListenConfig<PerformanceConfig>(value => SetCapacity((long)value.HistoryImageCacheSizeMB * 1_000_000));

    public void SetCapacity(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        lock (_gate)
        {
            _capacity = bytes;
            _bytes = 0;
            foreach (var entry in _entries.Values.ToArray())
            {
                entry.Counted = entry.Image is not null && CanRetain(entry.Bytes);
                if (entry.Counted)
                    _bytes += entry.Bytes;
                else if (entry.Image is not null && entry.Users == 0)
                    Remove(entry);
            }
            Trim();
        }
    }

    public async Task<PreviewImageLease<T>> AcquireAsync(TKey key, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        Entry entry;
        bool start;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            start = !_entries.TryGetValue(key, out entry!);
            if (start)
            {
                entry = new Entry(key);
                _entries.Add(key, entry);
            }
            if (entry.IdleNode is { } node)
            {
                _idle.Remove(node);
                entry.IdleNode = null;
            }
            entry.Users++;
        }

        if (start)
            _ = LoadAsync(entry);

        try
        {
            await entry.Completion.Task.WaitAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return new PreviewImageLease<T>(entry.Image!, () => Return(entry));
        }
        catch
        {
            Return(entry);
            throw;
        }
    }

    private async Task LoadAsync(Entry entry)
    {
        T? image = null;
        try
        {
            image = await _load(entry.Key, entry.Cancellation.Token);
            // Charge a minimum per entry so metadata-only previews cannot grow without bound.
            var bytes = Math.Max(256, _measure(image));
            lock (_gate)
            {
                entry.Image = image;
                image = null;
                entry.Bytes = bytes;
                entry.Counted = !entry.Invalid && CanRetain(bytes);
                if (entry.Counted)
                    _bytes += bytes;
                if (entry.Users == 0)
                    Remove(entry);
                Trim();
                entry.Completion.TrySetResult();
            }
        }
        catch (Exception exception)
        {
            if (image is not null)
                _release(image);
            lock (_gate)
            {
                entry.Invalid = true;
                RemoveKey(entry);
                entry.Completion.TrySetException(exception);
                // All waiters may have canceled before the decoder failed.
                _ = entry.Completion.Task.Exception;
            }
        }
        finally
        {
            lock (_gate)
                entry.Cancellation.Dispose();
        }
    }

    private void Return(Entry entry)
    {
        lock (_gate)
        {
            entry.Users--;
            if (entry.Users != 0)
                return;

            if (entry.Image is null)
            {
                entry.Invalid = true;
                RemoveKey(entry);
                if (!entry.Completion.Task.IsCompleted)
                    entry.Cancellation.Cancel();
            }
            else if (entry.Invalid || _disposed || !entry.Counted)
            {
                Remove(entry);
            }
            else
            {
                entry.IdleNode = _idle.AddLast(entry);
                Trim();
            }
        }
    }

    /// <summary>Stops reusing matching entries. Existing leases remain valid until returned.</summary>
    public void Invalidate(Func<TKey, bool> matches)
    {
        lock (_gate)
        {
            foreach (var entry in _entries.Values.Where(entry => matches(entry.Key)).ToArray())
            {
                entry.Invalid = true;
                Uncount(entry);
                RemoveKey(entry);
                if (entry.Users == 0)
                    Remove(entry);
            }
        }
    }

    // Avoid overflowing long while taking floor(capacity * 0.8).
    private bool CanRetain(long bytes) => bytes <= _capacity / 5 * 4 + _capacity % 5 * 4 / 5;

    private void Uncount(Entry entry)
    {
        if (entry.Counted)
        {
            _bytes -= entry.Bytes;
            entry.Counted = false;
        }
    }

    private void Trim()
    {
        while (_bytes > _capacity && _idle.First is { } node)
            Remove(node.Value);
    }

    private void RemoveKey(Entry entry)
    {
        if (_entries.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry))
            _entries.Remove(entry.Key);
    }

    private void Remove(Entry entry)
    {
        RemoveKey(entry);
        if (entry.IdleNode is { } node)
        {
            _idle.Remove(node);
            entry.IdleNode = null;
        }
        if (entry.Image is { } image)
        {
            entry.Image = null;
            Uncount(entry);
            _release(image);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Invalidate(_ => true);
        }
    }
}
