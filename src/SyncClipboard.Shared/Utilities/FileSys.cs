namespace SyncClipboard.Shared.Utilities;

public static class FileSys
{
    public static Task<bool> FileExistsAsync(string path)
    {
        return Task.Run(() => File.Exists(path));
    }

    public static Task<bool> DirectoryExistsAsync(string path)
    {
        return Task.Run(() => Directory.Exists(path));
    }

    public static Task<DirectoryInfo> CreateDirectoryAsync(string path)
    {
        return Task.Run(() => Directory.CreateDirectory(path));
    }

    public static Task<string[]> GetFilesAsync(string path)
    {
        return Task.Run(() => Directory.GetFiles(path));
    }

    public static Task<string[]> GetFilesAsync(string path, string searchPattern)
    {
        return Task.Run(() => Directory.GetFiles(path, searchPattern));
    }

    public static Task<string[]> GetFilesAsync(string path, string searchPattern, SearchOption searchOption)
    {
        return Task.Run(() => Directory.GetFiles(path, searchPattern, searchOption));
    }

    public static Task<string[]> GetDirectoriesAsync(string path)
    {
        return Task.Run(() => Directory.GetDirectories(path));
    }

    public static Task<string[]> GetDirectoriesAsync(string path, string searchPattern)
    {
        return Task.Run(() => Directory.GetDirectories(path, searchPattern));
    }

    public static Task<string[]> GetDirectoriesAsync(string path, string searchPattern, SearchOption searchOption)
    {
        return Task.Run(() => Directory.GetDirectories(path, searchPattern, searchOption));
    }

    public static Task DeleteDirectoryAsync(string path)
    {
        return Task.Run(() => Directory.Delete(path));
    }

    public static Task DeleteDirectoryAsync(string path, bool recursive)
    {
        return Task.Run(() => Directory.Delete(path, recursive));
    }

    public static Task FileCopyAsync(string sourceFileName, string destFileName, CancellationToken token)
    {
        return FileCopyAsync(sourceFileName, destFileName, overwrite: false, token);
    }

    public static async Task FileCopyAsync(string sourceFileName, string destFileName, bool overwrite, CancellationToken token)
    {
        var openForReading = new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        };
        await using var source = new FileStream(sourceFileName, openForReading);

        var createForWriting = new FileStreamOptions
        {
            Mode = overwrite ? FileMode.Create : FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
            BufferSize = 0,
            PreallocationSize = source.Length
        };
        await using var destination = new FileStream(destFileName, createForWriting);

        await source.CopyToAsync(destination, token).ConfigureAwait(false);
    }
}
