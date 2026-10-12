namespace SyncClipboard.Core.Utilities.History.HistoryImport;

internal static class HistoryImportAttachmentCleanup
{
    internal static void RemoveObsolete(string root, IEnumerable<string> oldPaths, IEnumerable<string> currentPaths)
    {
        root = Path.GetFullPath(root);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var current = currentPaths.Select(Path.GetFullPath).ToArray();
        foreach (var value in oldPaths.Distinct())
        {
            try
            {
                var path = Path.GetFullPath(value);
                if (!IsWithin(path, root, comparison) || path.Equals(root, comparison) ||
                    current.Any(keep => IsWithin(keep, path, comparison) || IsWithin(path, keep, comparison)) ||
                    HasLinkAncestor(path, root))
                    continue;
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                else
                    File.Delete(path);
                RemoveEmptyParents(Path.GetDirectoryName(path), root, comparison);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
        }
    }

    private static bool IsWithin(string path, string root, StringComparison comparison) =>
        path.Equals(root, comparison) || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison);

    private static bool HasLinkAncestor(string path, string root)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                return true;
            if (current == root)
                return false;
        }
        return true;
    }

    private static void RemoveEmptyParents(string? path, string root, StringComparison comparison)
    {
        while (path is not null && !path.Equals(root, comparison) && IsWithin(path, root, comparison))
        {
            Directory.Delete(path, recursive: false);
            path = Path.GetDirectoryName(path);
        }
    }
}
