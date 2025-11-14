namespace SyncClipboard.Shared.Utilities;

public sealed class ByteArrayComparer : IComparer<byte[]>
{
    public static readonly ByteArrayComparer Instance = new();

    private ByteArrayComparer() { }

    public int Compare(byte[]? x, byte[]? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        int len = Math.Min(x.Length, y.Length);
        for (int i = 0; i < len; i++)
        {
            int diff = x[i].CompareTo(y[i]);
            if (diff != 0) return diff;
        }
        return x.Length.CompareTo(y.Length);
    }
}
