using Ionic.Zip;
using SyncClipboard.Abstract;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using System.Text;

namespace SyncClipboard.Core.Clipboard;

public class GroupProfile : FileProfile
{
    private readonly FileFilterConfig _fileFilterConfig = new();
    private string[]? _files;
    public string[] Files => _files ?? [];

    public override ProfileType Type => ProfileType.Group;

    private GroupProfile(IEnumerable<string> files, string hash)
        : base(null, CreateNewDataFileName(), hash)
    {
        _files = [.. files];
    }

    private GroupProfile(IEnumerable<string> files, FileFilterConfig filterConfig)
        : base(null, CreateNewDataFileName(), null)
    {
        _files = [.. files];
        _fileFilterConfig = filterConfig;
    }

    private static string CreateNewDataFileName()
    {
        return $"File_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Path.GetRandomFileName()}.zip";
    }

    public GroupProfile(ClipboardProfileDTO profileDTO) : base(profileDTO)
    {
    }

    public GroupProfile(HistoryRecord record) : this(record.FilePath, record.Hash)
    {
    }

    public static Task<Profile> Create(string[] files, FileFilterConfig? filterConfig = null)
    {
        filterConfig ??= new FileFilterConfig();
        return Task.FromResult<Profile>(new GroupProfile(files, filterConfig));
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
                    sumSize += subFile.Length;
                    if (sumSize > MAX_FILE_SIZE)
                        return (MD5_FOR_OVERSIZED_FILE, 0);
                    hash = (hash * -1521134295) + (subFile.Name + subFile.Length.ToString()).ListHashCode();
                }
            }
            else if (File.Exists(file) && ContentControlHelper.IsFileAvailableAfterFilter(file, _fileFilterConfig))
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
        var hash = await GetHash(token);
        await Task.Run(() =>
        {
            using ZipFile zip = new ZipFile();
            zip.AlternateEncoding = Encoding.UTF8;
            zip.AlternateEncodingUsage = ZipOption.AsNecessary;

            ArgumentNullException.ThrowIfNull(_files);
            _files.ForEach(path =>
            {
                token.ThrowIfCancellationRequested();
                if (Directory.Exists(path))
                {
                    zip.AddDirectory(path, Path.GetFileName(path));
                }
                else if (File.Exists(path))
                {
                    zip.AddItem(path, "");
                }
            });

            foreach (var item in zip.Entries)
            {
                if (!item.IsDirectory && !ContentControlHelper.IsFileAvailableAfterFilter(item.FileName, _fileFilterConfig))
                {
                    zip.RemoveEntry(item.FileName);
                }
            }

            zip.Save(filePath);
            FullPath = filePath;
        }, token).WaitAsync(token);
    }

    public override string ShowcaseText()
    {
        if (_files is null)
            return string.Empty;

        if (_files.Length > 5)
        {
            return string.Join("\n", _files.Take(5).Select(file => Path.GetFileName(file))) + "\n...";
        }
        return string.Join("\n", _files.Select(file => Path.GetFileName(file)));
    }

    public override async Task CheckDownloadedData(CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(FullPath);
        ArgumentNullException.ThrowIfNull(_hash);

        if (!File.Exists(FullPath))
        {
            throw new FileNotFoundException($"Zip file does not exist: {FullPath}", FullPath);
        }

        var extractPath = FullPath[..^4];
        if (!Directory.Exists(extractPath))
            Directory.CreateDirectory(extractPath);

        using ZipFile zip = ZipFile.Read(FullPath);
        await Task.Run(() => zip.ExtractAll(extractPath, ExtractExistingFileAction.DoNotOverwrite), token).WaitAsync(token);

        _files = zip.EntryFileNames
            .Select(file => file.TrimEnd('/'))
            .Where(file => !file.Contains('/'))
            .Select(file => Path.Combine(extractPath, file))
            .ToArray();

        var (hash, _) = await CaclHashAndSizeAsync(_files, token);
        if (hash != _hash)
        {
            var errorMsg = $"Group data hash mismatch. Expected: {_hash}, Actual: {hash}";
            throw new InvalidDataException(errorMsg);
        }
    }

    public override async Task<bool> ValidLocalData(bool quick, CancellationToken token)
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
