namespace SyncClipboard.Core.Utilities;

public static class IEnumerableExtention
{
    public static int ListHashCode<T>(this IEnumerable<T> list) where T : notnull
    {
        var comparer = EqualityComparer<T>.Default;
        return list.Select(item => comparer.GetHashCode(item)).ListHashCode();
    }

    public static int ListHashCode(this IEnumerable<int> list)
    {
        int hash = 0;
        foreach (int item in list)
        {
            hash += (hash * -1521134295) + item;
        }
        return hash;
    }
}
