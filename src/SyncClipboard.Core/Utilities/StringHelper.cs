namespace SyncClipboard.Core.Utilities;

public static class StringHelper
{
    public static string GetHostFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url ?? string.Empty;

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
            return uri.Host;

        if (Uri.TryCreate("http://" + url, UriKind.Absolute, out uri) && !string.IsNullOrEmpty(uri.Host))
            return uri.Host;

        var idx = url.IndexOf(':');
        if (idx > 0)
            return url[..idx];

        return url;
    }
}
