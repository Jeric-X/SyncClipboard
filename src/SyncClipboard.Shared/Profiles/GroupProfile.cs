using System.IO.Compression;
using SyncClipboard.Shared.Models;
using SyncClipboard.Shared.Utilities;
using System.Text;

namespace SyncClipboard.Shared.Profiles;

public class GroupProfile : FileProfile
{
    private readonly FileFilterConfig _fileFilterConfig = new();
    private string[]? _files;
    public string[] Files => _files ?? [];
    public override string Text => string.Join('\n', Files.Select(Path.GetFileName));

    public override ProfileType Type => ProfileType.Group;

    public GroupProfile(IEnumerable<string> files, string hash, string? dataPath = null)
        : base(null, CreateNewDataFileName(), hash)
    {
        _files = [.. files];
        FullPath = dataPath;
    }

    public GroupProfile(IEnumerable<string> files, FileFilterConfig? filterConfig = null)
        : base(null, CreateNewDataFileName(), null)
    {
        _files = [.. files];
        _fileFilterConfig = filterConfig ?? new();
    }

    public GroupProfile(string hash)
        : base(null, CreateNewDataFileName(), hash)
    {
    }

    private static string CreateNewDataFileName()
    {
        return $"File_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Path.GetRandomFileName()}.zip";
    }

    public GroupProfile(ClipboardProfileDTO profileDTO) : base(profileDTO)
    {
    }

    private static int FileCompare(FileInfo file1, FileInfo file2)
    {
        if (file1.Length == file2.Length)
        {
            return Comparer<int>.Default.Compare(file1.Name.ListHashCode(), file2.Name.ListHashCode());
        }
        return Comparer<long>.Default.Compare(file1.Length, file2.Length);
    }

    private static int FileNameCompare(string file1, string file2)
    {
        return Comparer<int>.Default.Compare(
            Path.GetFileName(file1).ListHashCode(),
            Path.GetFileName(file2).ListHashCode()
        );
    }

    public override async ValueTask<string> GetHash(CancellationToken token)
    {
        if (_hash is null)
        {
            (_hash, _size) = await CaclHashAndSizeAsync(_files ?? [], token);
        }

        return _hash;
    }

    public override async ValueTask<long> GetSize(CancellationToken token)
    {
        if (_size is null)
        {
            (_hash, _size) = await CaclHashAndSizeAsync(_files ?? [], token); ;
        }

        return _size.Value;
    }

    private Task<(string, long)> CaclHashAndSizeAsync(IEnumerable<string> filesEnum, CancellationToken token)
    {
        return Task.Run(() => CaclHashAndSize(filesEnum, token), token).WaitAsync(token);
    }

    private (string, long) CaclHashAndSize(IEnumerable<string> filesEnum, CancellationToken token)
    {
        var files = filesEnum.ToArray();
        Array.Sort(files, FileNameCompare);
        long sumSize = 0;
        int hash = 0;
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(file))
            {
                var directoryInfo = new DirectoryInfo(file);
                hash = (hash * -1521134295) + directoryInfo.Name.ListHashCode();
                var subFiles = directoryInfo.GetFiles("*", SearchOption.AllDirectories);
                Array.Sort(subFiles, FileCompare);
                foreach (var subFile in subFiles)
                {
                    token.ThrowIfCancellationRequested();
                    sumSize += subFile.Length;
                    if (sumSize > MAX_FILE_SIZE)
                        return (MD5_FOR_OVERSIZED_FILE, 0);
                    hash = (hash * -1521134295) + (subFile.Name + subFile.Length.ToString()).ListHashCode();
                }
            }
            else if (File.Exists(file) && FileFilterHelper.IsFileAvailableAfterFilter(file, _fileFilterConfig))
            {
                var fileInfo = new FileInfo(file);
                sumSize += fileInfo.Length;
                hash = (hash * -1521134295) + (fileInfo.Name + fileInfo.Length.ToString()).ListHashCode();
            }

            if (sumSize > MAX_FILE_SIZE)
            {
                return (MD5_FOR_OVERSIZED_FILE, 0);
            }
        }

        return (hash.ToString(), sumSize);
    }

    public async Task PrepareTransferFile(string filePath, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(_files);

        await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false, entryNameEncoding: Encoding.UTF8);

        foreach (var path in _files)
        {
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(path))
            {
                var dirName = Path.GetFileName(path);
                archive.CreateEntry(dirName + "/");

                var subDirs = Directory.GetDirectories(path, "*", SearchOption.AllDirectories);
                foreach (var subDir in subDirs)
                {
                    token.ThrowIfCancellationRequested();
                    var relativeDir = Path.GetRelativePath(path, subDir).Replace(Path.DirectorySeparatorChar, '/');
                    var dirEntryName = string.Join('/', [dirName, relativeDir]) + "/";
                    if (FileFilterHelper.IsFileAvailableAfterFilter(dirEntryName, _fileFilterConfig))
                        archive.CreateEntry(dirEntryName);
                }

                var subFiles = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                foreach (var subFile in subFiles)
                {
                    token.ThrowIfCancellationRequested();
                    var relativePath = Path.GetRelativePath(path, subFile).Replace(Path.DirectorySeparatorChar, '/');
                    var entryName = string.Join('/', [dirName, relativePath]);
                    await AddFileToArchiveAsync(archive, entryName, subFile, token).ConfigureAwait(false);
                }
            }
            else if (File.Exists(path))
            {
                var entryName = Path.GetFileName(path);
                await AddFileToArchiveAsync(archive, entryName, path, token).ConfigureAwait(false);
            }
        }

        FullPath = filePath;
    }

    private async Task AddFileToArchiveAsync(ZipArchive archive, string entryName, string sourcePath, CancellationToken token)
    {
        if (!FileFilterHelper.IsFileAvailableAfterFilter(entryName, _fileFilterConfig))
            return;

        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await sourceStream.CopyToAsync(entryStream, 81920, token).ConfigureAwait(false);
    }

    public override string GetDisplayText()
    {
        if (_files is null)
            return string.Empty;

        if (_files.Length > 5)
        {
            return string.Join("\n", _files.Take(5).Select(file => Path.GetFileName(file))) + "\n...";
        }
        return string.Join("\n", _files.Select(file => Path.GetFileName(file)));
    }

    private static async Task<string[]> ExtractArchiveEntriesAsync(ZipArchive archive, string extractPath, CancellationToken token)
    {
        var topLevelFiles = new List<string>();
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var entryName = entry.FullName;
            var isDirectory = entryName.EndsWith('/');
            var destPath = Path.Combine(extractPath, entryName.Replace('/', Path.DirectorySeparatorChar));

            if (isDirectory)
            {
                if (!Directory.Exists(destPath))
                {
                    Directory.CreateDirectory(destPath);
                }
            }
            else if (!File.Exists(destPath))
            {
                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                await using var entryStream = entry.Open();
                await using var destStream = new FileStream(destPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await entryStream.CopyToAsync(destStream, 81920, token).ConfigureAwait(false);
            }

            var trimmed = entry.FullName.TrimEnd('/');
            if (!trimmed.Contains('/'))
            {
                topLevelFiles.Add(Path.Combine(extractPath, trimmed));
            }
        }

        return topLevelFiles.ToArray();
    }

    public override async Task SetTranseferData(string path, CancellationToken token)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Zip file does not exist: {path}", path);
        }

        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"File is not a zip archive: {path}");
        }

        ArgumentNullException.ThrowIfNull(_hash);

        var extractPath = path[..^4];
        if (!Directory.Exists(extractPath))
            Directory.CreateDirectory(extractPath);

        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: Encoding.UTF8);
        var topLevelFiles = await ExtractArchiveEntriesAsync(archive, extractPath, token).ConfigureAwait(false);
        _files = topLevelFiles;

        var (hash, _) = await CaclHashAndSizeAsync(_files, token);
        if (hash != _hash)
        {
            var errorMsg = $"Group data hash mismatch. Expected: {_hash}, Actual: {hash}";
            throw new InvalidDataException(errorMsg);
        }
        FullPath = path;
        FileName = Path.GetFileName(path);
    }

    public override async Task<bool> IsLocalDataValid(bool quick, CancellationToken token)
    {
        if (_files is null || _files.Length == 0)
            return false;

        foreach (var path in _files)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                return false;
        }

        if (quick)
            return true;

        if (_hash is null)
        {
            return true;
        }

        try
        {
            var (hash, _) = await CaclHashAndSizeAsync(_files, token);
            return hash == _hash;
        }
        catch
        {
            return false;
        }
    }
}
