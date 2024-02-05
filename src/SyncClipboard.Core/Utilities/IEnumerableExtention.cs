namespace SyncClipboard.Core.Utilities;

public static class IEnumerableExtention
{
    public static int ListHashCode<T>(this IEnumerable<T> list) where T : notnull
    {
        var comparer = EqualityComparer<T>.Default;
        int hash = 6551;
        foreach (T item in list)
        {
            hash ^= (hash << 5) ^ comparer.GetHashCode(item);
        }
        return hash;
    }
}
