namespace SyncClipboard.Core.Models;

public record class LocaleString(string String, string ShownString);

public record class LocaleString<T>
{
    public T Key { get; }
    public string ShownString { get; }

    public LocaleString(T obj, string shownString)
    {
        Key = obj;
        ShownString = shownString;
    }

    public static LocaleString<T> Match(IEnumerable<LocaleString<T>> localeStrings, T obj)
    {
        if (!localeStrings.Any())
        {
            throw new ArgumentNullException(nameof(localeStrings));
        }

        return localeStrings.FirstOrDefault(x => EqualityComparer<T>.Default.Equals(x.Key, obj)) ?? localeStrings.First();
    }
}
