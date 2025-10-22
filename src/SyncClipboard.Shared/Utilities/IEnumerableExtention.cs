namespace SyncClipboard.Shared.Utilities;

public static class IEnumerableExtention
{
    public static int ListHashCode<T>(this IEnumerable<T> list) where T : notnull
    {
        var comparer = EqualityComparer<T>.Default;
        return list.Select(item => comparer.GetHashCode(item)).ListHashCode();
    }

    public static int ListHashCode(this string list)
    {
        return list.Select(item => (int)item).ListHashCode();
    }

    public static int ListHashCode(this IEnumerable<int> list)
    {
        int hash = 0;
        foreach (int item in list)
        {
            hash = (hash * -1521134295) + item;
        }
        return hash;
    }

    public static void ForEach<T>(this IEnumerable<T> source, Action<T> action)
    {
        foreach (T item in source)
        {
            action.Invoke(item);
        }
    }
}
