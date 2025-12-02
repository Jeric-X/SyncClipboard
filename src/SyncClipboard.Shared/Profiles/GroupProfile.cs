using System.IO.Compression;
using SyncClipboard.Shared.Models;
using SyncClipboard.Shared.Utilities;
using System.Text;
using System.Security.Cryptography;
using System.Diagnostics.CodeAnalysis;
using SyncClipboard.Shared.Profiles.Models;

namespace SyncClipboard.Shared.Profiles;

public class GroupProfile : Profile
{
    private static readonly SemaphoreSlim ConcurrencyComputeLimiter = new(Math.Max(1, Environment.ProcessorCount));
    private static readonly Encoding EntryEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly FileFilterConfig _fileFilterConfig = new();
    public override bool HasTransferData => true;
    private string? _transferDataName = null;
    private string? _transferDataPath;
    private string[]? _files;
    public string[] Files => _files ?? [];

    public override ProfileType Type => ProfileType.Group;
    public override string DisplayText => GetDisplayText();

    public override string ShortDisplayText => GetDisplayText(true);

    public GroupProfile(ProfilePersistentInfo entity)
        : this(entity.FilePaths ?? [], entity.Hash, entity.TransferDataFile)
    {
    }

    public GroupProfile(IEnumerable<string> files, string hash, string? dataPath = null)
    {
        _files = [.. files];
        Hash = string.IsNullOrEmpty(hash) ? null : hash;
        _transferDataPath = dataPath;
    }

    public GroupProfile(IEnumerable<string> files, FileFilterConfig? filterConfig = null)
    {
        _files = [.. files];
        _fileFilterConfig = filterConfig ?? new();
    }

    private static string CreateNewDataFileName()
    {
        return $"File_{Utility.CreateTimeBasedFileName()}.zip";
    }

    public GroupProfile(ClipboardProfileDTO profileDTO)
    {
        _transferDataName = profileDTO.File;
        Hash = profileDTO.Clipboard;
    }

    protected override async Task ComputeHash(CancellationToken token)
    {
        var (hash, size) = await Task.Run(() => CaclHashAndSize(_files ?? [], token), token).WaitAsync(token);
        Hash = hash;
        Size ??= size;
    }

    protected override async Task ComputeSize(CancellationToken token)
    {
        var (hash, size) = await Task.Run(() => CaclHashAndSize(_files ?? [], token), token).WaitAsync(token);
        Size = size;
        Hash ??= hash;
    }

    /// <summary>
    /// 哈希算法说明：
    /// 1) 编码：所有字符串均使用UTF-8编码。
    /// 2) 获取Entry: 取inputFiles自身以及所有子目录/文件作为候选条目，每个entry以inputFiles的父目录作为
    ///    根取相对路径作为EntryName。
    /// 3) 排序：对所有entry排序，排序方法为：按EntryName的UTF-8编码后的byte数组升序排序。
    /// 4) 哈希：按照格式构造每个entry的哈希输入字符串，UTF-8编码后按序拼接成完整的哈希输入byte数组，
    ///    使用sha256计算得到的输出值即为当前GroupProfile实例的hash。
    ///    每个Entry的哈希输入字符串格式：
    ///      - 目录：{entryName}   (entryName以 '/' 结尾)
    ///      - 文件：{entryName}|{length}|{sha256(content)}
    /// 5) 由于每个文件都需要独立计算hash，可以考虑并发处理以提升性能。
    /// </summary>
    private async Task<(string, long)> CaclHashAndSize(IEnumerable<string> inputFiles, CancellationToken token)
    {
        long totalSize = 0;
        var entries = new List<GroupEntry>();

        if (!inputFiles.Any())
        {
            var emptyHash = SHA256.HashData(EntryEncoding.GetBytes(string.Empty));
            return (Convert.ToHexString(emptyHash), 0);
        }

        var firstPath = inputFiles.First();
        var rootPath = ResolveRootPath(firstPath);

        var details = inputFiles.Select(path => new { IsDir = Directory.Exists(path), path });
        var dirs = details.Where(d => d.IsDir).Select(d => d.path).ToArray();
        var files = details.Where(d => !d.IsDir).Select(d => d.path).ToArray();

        SearchDirEntries(dirs, rootPath, ref totalSize, entries, token);
        AddEntries(files, rootPath, ref totalSize, entries, token);

        var orderedEntries = entries
            .Select(e => new { Entry = e, Key = EntryEncoding.GetBytes(e.EntryName) })
            .OrderBy(k => k.Key, ByteArrayComparer.Instance);

        using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var entry in orderedEntries)
        {
            token.ThrowIfCancellationRequested();

            var line = await entry.Entry.ToEntryStringAsync().ConfigureAwait(false);
            var bytes = EntryEncoding.GetBytes(line);
            incremental.AppendData(bytes);
        }

        var finalBytes = incremental.GetHashAndReset();
        return (Convert.ToHexString(finalBytes), totalSize);
    }

    private void SearchDirEntries(IEnumerable<string> dirs, string root, ref long totalSize, List<GroupEntry> entries, CancellationToken token)
    {
        foreach (var dir in dirs)
        {
            token.ThrowIfCancellationRequested();

            AddEntry(dir, root, ref totalSize, entries, token);

            var allDirectories = Directory.GetDirectories(dir, "*", SearchOption.AllDirectories);
            AddEntries(allDirectories, root, ref totalSize, entries, token);

            var allFiles = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
            AddEntries(allFiles, root, ref totalSize, entries, token);
        }
    }

    private static string ResolveRootPath(string firstPath)
    {
        if (!string.IsNullOrEmpty(firstPath))
        {
            var basePath = Directory.Exists(firstPath) ? Path.TrimEndingDirectorySeparator(firstPath) : firstPath;
            var parent = Path.GetDirectoryName(basePath);
            // 当处于文件系统根（如 Linux 的 "/"）时，父目录可能为 null，退回到路径根
            return parent ?? Path.GetPathRoot(basePath) ?? "/";
        }
        else
        {
            return Path.GetPathRoot(Directory.GetCurrentDirectory()) ?? "/";
        }
    }

    private void AddEntries(IEnumerable<string> paths, string root, ref long totalSize, List<GroupEntry> entries, CancellationToken token)
    {
        foreach (var path in paths)
        {
            AddEntry(path, root, ref totalSize, entries, token);
        }
    }

    private void AddEntry(string path, string root, ref long totalSize, List<GroupEntry> entries, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        bool isDir = Directory.Exists(path);
        bool isFile = !isDir && File.Exists(path);
        if (!isDir && !isFile)
            return;

        var entryName = BuildRelativeEntryName(root, path, isDirectory: isDir);
        if (string.IsNullOrEmpty(entryName))
            return;

        if (!FileFilterHelper.IsFileAvailableAfterFilter(entryName, _fileFilterConfig))
            return;

        if (isDir)
        {
            entries.Add(new GroupEntry(entryName, isDirectory: true, length: 0, hashTask: null));
        }
        else
        {
            var fileInfo = new FileInfo(path);
            totalSize += fileInfo.Length;
            var hashTask = ComputeFileContentHashAsync(path, token);
            entries.Add(new GroupEntry(entryName, isDirectory: false, length: fileInfo.Length, hashTask: hashTask));
        }
    }

    private static string BuildRelativeEntryName(string rootPath, string fullPath, bool isDirectory)
    {
        var rel = Path.GetRelativePath(rootPath, fullPath);
        if (string.IsNullOrEmpty(rel) || rel == ".")
            return string.Empty;

        var normalized = rel
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/')
            .Trim('/');

        if (string.IsNullOrEmpty(normalized))
            return string.Empty;

        if (isDirectory)
            return normalized + "/";

        return normalized;
    }

    private static async Task<string> ComputeFileContentHashAsync(string filePath, CancellationToken token)
    {
        await ConcurrencyComputeLimiter.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Utility.CalculateFileSHA256(filePath, token);
        }
        finally
        {
            ConcurrencyComputeLimiter.Release();
        }
    }

    public override async Task<string?> PrepareTransferData(string persistentDir, CancellationToken token)
    {
        if (File.Exists(_transferDataPath))
        {
            return _transferDataPath;
        }

        ArgumentNullException.ThrowIfNull(_files);
        var fileName = _transferDataName ?? CreateNewDataFileName();
        var filePath = Path.Combine(GetWorkingDir(persistentDir, Type, await GetHash(token)), fileName);

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

        _transferDataName = fileName;
        _transferDataPath = filePath;
        return filePath;
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

    public override async Task<ClipboardProfileDTO> ToDto(CancellationToken token)
    {
        if (_transferDataName is null)
        {
            throw new InvalidOperationException("Transfer data is not set.");
        }
        return new ClipboardProfileDTO(_transferDataName, await GetHash(token), Type);
    }

    public string GetDisplayText(bool shortStr = false)
    {
        if (_files is null)
            return string.Empty;

        if (shortStr && _files.Length > 5)
        {
            return string.Join("\n", _files.Take(5).Select(file => Path.GetFileName(file))) + "\n...";
        }
        return string.Join("\n", _files.Select(file => Path.GetFileName(file)));
    }

    private static async Task<string[]> ExtractArchiveEntriesAsync(ZipArchive archive, string extractPath, CancellationToken token)
    {
        var topLevelFiles = new List<string>();
        extractPath = Path.GetFullPath(extractPath + Path.DirectorySeparatorChar);
        var comparision = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var entryName = entry.FullName;
            var isDirectory = entryName.EndsWith('/');
            var rawDestPath = Path.Combine(extractPath, entryName.Replace('/', Path.DirectorySeparatorChar));
            var destPath = Path.GetFullPath(rawDestPath);
            if (!destPath.StartsWith(extractPath, comparision))
            {
                throw new InvalidOperationException($"Transfer data is invalid with entry: {entryName}");
            }

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

    private async Task ExtractAndVerifyTransferData(string extractDir, string path, CancellationToken token)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: Encoding.UTF8);
        var topLevelFiles = await ExtractArchiveEntriesAsync(archive, extractDir, token).ConfigureAwait(false);
        _files = topLevelFiles;

        var (hash, size) = await Task.Run(() => CaclHashAndSize(_files, token), token).WaitAsync(token);
        if (Hash is not null && hash != Hash)
        {
            var errorMsg = $"Group data hash mismatch. Expected: {Hash}, Actual: {hash}";
            throw new InvalidDataException(errorMsg);
        }
        Hash = hash;
        Size = size;
        _transferDataPath = path;
        _transferDataName = Path.GetFileName(path);
    }

    public override Task SetTranseferData(string path, bool verify, CancellationToken token)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Zip file does not exist: {path}", path);
        }

        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"File is not a zip archive: {path}");
        }

        if (!verify)
        {
            _transferDataPath = path;
            _transferDataName = Path.GetFileName(path);
            return Task.CompletedTask;
        }

        var extractDir = path[..^4];
        if (!Directory.Exists(extractDir))
            Directory.CreateDirectory(extractDir);
        return ExtractAndVerifyTransferData(extractDir, path, token);
    }

    public override async Task SetAndMoveTransferData(string persistentDir, string path, CancellationToken token)
    {
        if (File.Exists(_transferDataPath))
        {
            return;
        }

        await SetTranseferData(path, true, token);

        var workingDir = GetWorkingDir(persistentDir, Type, Hash!);
        var persistentPath = GetPersistentPath(workingDir, path);

        if (Path.IsPathRooted(persistentPath!) is false)
        {
            return;
        }

        var targetPath = Path.Combine(workingDir, _transferDataName!);
        File.Move(path, targetPath, true);
        try
        {
            Directory.Move(path[..^4], targetPath[..^4]);
        }
        catch { }
        _transferDataPath = targetPath;
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

        if (Hash is null)
        {
            return true;
        }

        try
        {
            var (hash, _) = await Task.Run(() => CaclHashAndSize(_files, token), token).WaitAsync(token);
            return string.Equals(hash, Hash, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public override bool NeedsTransferData(string persistentDir, [NotNullWhen(true)] out string? dataPath)
    {
        if (_transferDataPath is null && Hash is not null)
        {
            var fileName = _transferDataName ?? CreateNewDataFileName();
            dataPath = Path.Combine(GetWorkingDir(persistentDir, Hash ?? string.Empty), fileName);
            return true;
        }
        dataPath = null;
        return false;
    }

    public override async Task<ProfilePersistentInfo> Persist(string persistentDir, CancellationToken token)
    {
        if (_files is null && _transferDataPath is null)
        {
            throw new InvalidOperationException("No local data available to prepare persistent storage.");
        }

        var workingDir = GetWorkingDir(persistentDir, Type, await GetHash(token));

        var relativeFiles = _files?
                            .Select(f => f.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                            .Select(f => Path.GetFileName(f))
                            .Where(f => string.IsNullOrEmpty(f) is false)
                            .ToArray() ?? [];
        var text = string.Join('\n', relativeFiles);

        return new ProfilePersistentInfo
        {
            Type = Type,
            Text = text,
            Size = await GetSize(token),
            Hash = await GetHash(token),
            TransferDataFile = GetPersistentPath(workingDir, _transferDataPath),
            FilePaths = _files?.Select(f => GetPersistentPath(workingDir, f))
                            .Where(f => string.IsNullOrEmpty(f) is false)
                            .ToArray() ?? []
        };
    }

    public override async Task<ProfileLocalInfo> Localize(string localDir, CancellationToken token)
    {
        if (_files is null && _transferDataPath is not null)
        {
            await SetAndMoveTransferData(localDir, _transferDataPath, token);
        }
        ArgumentNullException.ThrowIfNull(_files);

        return new ProfileLocalInfo
        {
            Text = string.Join('\n', _files),
            FilePaths = _files,
        };
    }
}
