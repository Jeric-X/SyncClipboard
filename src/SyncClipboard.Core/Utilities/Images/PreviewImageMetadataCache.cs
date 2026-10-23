namespace SyncClipboard.Core.Utilities.Images;

/// <summary>Small file-versioned LRU for dimensions, independent of decoded bitmap retention.</summary>
public sealed class PreviewImageMetadataCache<T> where T : struct
{
    private readonly Lock _gate = new();
    private readonly Dictionary<FileVersion, Entry> _entries = [];
    private readonly LinkedList<FileVersion> _recent = [];
    private const int Capacity = 1024;

    private readonly record struct FileVersion(string Path, long Length, long Ticks);
    private sealed record Entry(T Value, LinkedListNode<FileVersion> Node);

    public T? Get(PreviewImageKey key)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(new FileVersion(key.Path, key.Length, key.LastWriteTicks), out var entry))
                return null;
            _recent.Remove(entry.Node);
            _recent.AddLast(entry.Node);
            return entry.Value;
        }
    }

    public void Set(PreviewImageKey key, T value)
    {
        lock (_gate)
        {
            var file = new FileVersion(key.Path, key.Length, key.LastWriteTicks);
            if (_entries.Remove(file, out var old))
                _recent.Remove(old.Node);
            _entries.Add(file, new Entry(value, _recent.AddLast(file)));
            if (_recent.Count > Capacity && _recent.First is { } first)
            {
                _entries.Remove(first.Value);
                _recent.RemoveFirst();
            }
        }
    }
}
