namespace SyncClipboard.Core.Utilities;

public sealed class ThreadSafeDeque<T>
{
    private readonly System.Collections.Generic.LinkedList<T> _list = new();
    private readonly object _lock = new();

    public void EnqueueTail(T item)
    {
        lock (_lock)
        {
            _list.AddLast(item);
        }
    }

    public void EnqueueHead(T item)
    {
        lock (_lock)
        {
            _list.AddFirst(item);
        }
    }

    public bool TryDequeue(out T item)
    {
        lock (_lock)
        {
            if (_list.Count == 0)
            {
                item = default!;
                return false;
            }
            item = _list.First!.Value;
            _list.RemoveFirst();
            return true;
        }
    }

    public bool IsEmpty
    {
        get
        {
            lock (_lock)
            {
                return _list.Count == 0;
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _list.Clear();
        }
    }
}