namespace SyncClipboard.Core.Utilities.Updater;

internal static class MacUpdaterFiles
{
    internal static readonly string[] Libraries = ["libSkiaSharp.dylib", "libHarfBuzzSharp.dylib", "libAvaloniaNative.dylib"];

    public static string? FindBundle(string programDirectory)
    {
        for (var directory = new DirectoryInfo(programDirectory); directory is not null; directory = directory.Parent)
        {
            if (directory.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(directory.FullName, "Contents", "Info.plist")))
                return directory.FullName;
        }
        return null;
    }

    public static string[] GetFiles(string bundle)
    {
        var contents = Path.Combine(bundle, "Contents");
        var files = new List<string> { Path.Combine(contents, "Resources", "Updater", "SyncClipboard.Updater") };
        foreach (var library in Libraries)
        {
            var path = Directory.EnumerateFiles(contents, library, SearchOption.AllDirectories).FirstOrDefault();
            if (path is null)
                return [];
            files.Add(path);
        }
        return files.All(File.Exists) ? [.. files] : [];
    }
}
