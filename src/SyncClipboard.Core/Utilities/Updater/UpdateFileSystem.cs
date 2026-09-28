namespace SyncClipboard.Core.Utilities.Updater;

internal static class UpdateFileSystem
{
    public static bool IsWithin(string path, string directory)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(directory), Path.GetFullPath(path));
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    public static void CheckSpace(string directory, long bytes)
    {
        var fullPath = Path.GetFullPath(directory);
        var drive = DriveInfo.GetDrives().Where(d => d.IsReady && IsWithin(fullPath, d.RootDirectory.FullName))
            .OrderByDescending(d => d.RootDirectory.FullName.Length).FirstOrDefault();
        if (drive is not null && drive.AvailableFreeSpace < bytes + (32L * 1024 * 1024))
        {
            throw new IOException(I18n.Strings.UpdateInsufficientSpace);
        }
    }

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

    internal static bool HasLinkedAncestor(string path)
    {
        for (FileSystemInfo? item = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            item is not null; item = item is DirectoryInfo dir ? dir.Parent : ((FileInfo)item).Directory)
        {
            // Installation locations must not traverse symbolic links or junctions.
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        }
        return false;
    }

    internal static UpdateInstallCapability GetLocationCapability(UpdatePackageKind kind, string? target)
    {
        if (target is null || target.Any(char.IsControl) || (!Directory.Exists(target) && !File.Exists(target)) || HasLinkedAncestor(target))
        {
            return new(UpdatePackageKind.Unsupported, string.Empty, I18n.Strings.UpdateLocationUnsupported);
        }
        return new(kind, target);
    }

    internal static async Task ExtractScriptAsync(string directory, string name, CancellationToken token)
    {
        await using var resource = typeof(UpdateFileSystem).Assembly.GetManifestResourceStream(
            "SyncClipboard.Core.Utilities.Updater.Strategies.Scripts." + name)
            ?? throw new IOException("The embedded update script is missing: " + name);
        await using var output = File.Create(Path.Combine(directory, name));
        await resource.CopyToAsync(output, token);
    }
}
