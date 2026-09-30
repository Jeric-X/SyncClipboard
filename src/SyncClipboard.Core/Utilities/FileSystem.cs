namespace SyncClipboard.Core.Utilities;

internal static class FileSystem
{
    public static bool IsWithin(string path, string directory)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(directory), Path.GetFullPath(path));
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    public static bool HasEnoughSpace(string directory, long bytes)
    {
        var fullPath = Path.GetFullPath(directory);
        var drive = DriveInfo.GetDrives().Where(d => d.IsReady && IsWithin(fullPath, d.RootDirectory.FullName))
            .OrderByDescending(d => d.RootDirectory.FullName.Length).FirstOrDefault();
        return drive is null || drive.AvailableFreeSpace >= bytes;
    }

    public static long GetSize(string path) => Directory.Exists(path)
        ? Directory.EnumerateFiles(path, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        }).Sum(file => new FileInfo(file).Length)
        : new FileInfo(path).Length;

    public static bool CanWrite(string directory)
    {
        var probe = Path.Combine(directory, ".syncclipboard-write-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1,
                FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool HasLinkedAncestor(string path)
    {
        for (FileSystemInfo? item = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            item is not null; item = item is DirectoryInfo dir ? dir.Parent : ((FileInfo)item).Directory)
        {
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        }
        return false;
    }
}
