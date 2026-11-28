namespace SyncClipboard.Server.Core.Services;

internal sealed class UploadDirectory : IDisposable
{
    private readonly FileStream _lease;
    public string DirectoryPath { get; }

    public UploadDirectory(string historyDir)
    {
        var root = Directory.CreateDirectory(Path.Combine(historyDir, ".pending"));
        DirectoryPath = Path.Combine(root.FullName, Guid.NewGuid().ToString("N"));
        _lease = new FileStream(DirectoryPath + ".lock", FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            Directory.CreateDirectory(DirectoryPath);
        }
        catch
        {
            ReleaseLease();
            throw;
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(DirectoryPath, true);
        }
        finally
        {
            ReleaseLease();
        }
    }

    private void ReleaseLease()
    {
        _lease.Dispose();
        try
        {
            File.Delete(_lease.Name);
        }
        catch (IOException)
        {
            // A concurrent startup cleanup can already own this lock file and remove it.
        }
    }

    public static void CleanAbandoned(string historyDir, ILogger? logger)
    {
        var root = Path.Combine(historyDir, ".pending");
        if (!Directory.Exists(root))
            return;
        foreach (var path in Directory.EnumerateFiles(root, "*.lock"))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _))
                continue;
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    var directory = Path.ChangeExtension(path, null);
                    if (Directory.Exists(directory))
                        Directory.Delete(directory, true);
                }
                File.Delete(path);
            }
            catch (IOException ex)
            {
                logger?.LogDebug(ex, "Upload {Path} is active or unavailable for cleanup", path);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger?.LogWarning(ex, "Cannot clean upload {Path}", path);
            }
        }
    }
}
