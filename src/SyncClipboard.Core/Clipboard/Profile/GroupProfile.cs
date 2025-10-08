using Ionic.Zip;
using Microsoft.Extensions.DependencyInjection;
using NativeNotification.Interface;
using SyncClipboard.Abstract;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.FileCacheManager;
using System.Text;

namespace SyncClipboard.Core.Clipboard;

public class GroupProfile : FileProfile
{
    private string[]? _files;
    public string[] Files => _files ?? [];

    public override ProfileType Type => ProfileType.Group;

    protected override IClipboardSetter<Profile> ClipboardSetter
        => ServiceProvider.GetRequiredService<IClipboardSetter<GroupProfile>>();

    private GroupProfile(IEnumerable<string> files, string hash, bool contentControl)
        : base(GetGroupFilePath(hash), hash)
    {
        _files = [.. files];
        ContentControl = contentControl;
    }

    private static string GetGroupFilePath(string hash)
    {
        var historyConfig = Config.GetConfig<HistoryConfig>();
        if (historyConfig.EnableHistory)
        {
            var historyFolder = Path.Combine(Env.HistoryFileFolder, hash);
            Directory.CreateDirectory(historyFolder);
            return Path.Combine(historyFolder, $"File_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Path.GetRandomFileName()}.zip");
        }
        else
        {
            return Path.Combine(LocalTemplateFolder, $"File_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Path.GetRandomFileName()}.zip");
        }
    }

    public GroupProfile(ClipboardProfileDTO profileDTO) : base(profileDTO)
    {
    }

    public static async Task<Profile> Create(string[] files, bool contentControl, CancellationToken token)
    {
        if (contentControl)
        {
            var filterdFiles = files.Where(file => IsFileAvailableAfterFilter(file));
            if (filterdFiles.Count() == 1 && File.Exists(filterdFiles.First()))
                return await Create(filterdFiles.First(), contentControl, token);

            var hash = await CaclHashAsync(filterdFiles, contentControl, token);
            return new GroupProfile(filterdFiles, hash, contentControl);
        }
        else
        {
            var hash = await CaclHashAsync(files, contentControl, token);
            return new GroupProfile(files, hash, contentControl);
        }
    }

    public static async Task<Profile> Create(string[] files, CancellationToken token)
    {
        var filterdFiles = files.Where(file => IsFileAvailableAfterFilter(file));
        if (filterdFiles.Count() == 1 && File.Exists(filterdFiles.First()))
            return await Create(filterdFiles.First(), true, token);

        var hash = await CaclHashAsync(filterdFiles, true, token);
        return new GroupProfile(filterdFiles, hash, true);
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

    private static Task<string> CaclHashAsync(IEnumerable<string> filesEnum, bool contentControl, CancellationToken token)
    {
        return Task.Run(() => CaclHash(filesEnum, contentControl, token), token).WaitAsync(token);
    }

    private static string CaclHash(IEnumerable<string> filesEnum, bool contentControl, CancellationToken token)
    {
        var files = filesEnum.ToArray();
        var maxSize = Config.GetConfig<SyncConfig>().MaxFileByte;
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
                    if (contentControl && sumSize > maxSize)
                        return MD5_FOR_OVERSIZED_FILE;
                    hash = (hash * -1521134295) + (subFile.Name + subFile.Length.ToString()).ListHashCode();
                }
            }
            else if (File.Exists(file) && (!contentControl || IsFileAvailableAfterFilter(file)))
            {
                var fileInfo = new FileInfo(file);
                sumSize += fileInfo.Length;
                hash = (hash * -1521134295) + (fileInfo.Name + fileInfo.Length.ToString()).ListHashCode();
            }

            if (contentControl && sumSize > maxSize)
            {
                return MD5_FOR_OVERSIZED_FILE;
            }
        }

        return hash.ToString();
    }

    public override bool HasDataFile => true;
    public override bool RequiresPrepareData => true;

    public override async Task PrepareDataAsync(CancellationToken cancellationToken = default)
    {
        var cacheManager = ServiceProvider.GetRequiredService<LocalFileCacheManager>();
        var cachedZipPath = await cacheManager.GetCachedFilePathAsync(nameof(GroupProfile), Hash);
        if (!string.IsNullOrEmpty(cachedZipPath))
        {
            FullPath = cachedZipPath;
            return;
        }

        await PrepareTransferFile(cancellationToken);

        if (!string.IsNullOrEmpty(FullPath))
        {
            await cacheManager.SaveCacheEntryAsync(nameof(GroupProfile), Hash, FullPath);
        }
    }

    public Task PrepareTransferFile(CancellationToken token)
    {
        return Task.Run(() =>
        {
            var filePath = GetTempLocalFilePath();

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

            if (ContentControl)
            {
                foreach (var item in zip.Entries)
                {
                    if (!item.IsDirectory && !IsFileAvailableAfterFilter(item.FileName))
                    {
                        zip.RemoveEntry(item.FileName);
                    }
                }
            }
            zip.Save(filePath);
            FullPath = filePath;
        }, token).WaitAsync(token);
    }

    public async Task ExtractFiles(CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(FullPath);
        var extractPath = FullPath[..^4];
        if (!Directory.Exists(extractPath))
            Directory.CreateDirectory(extractPath);

        var fileList = new List<string>();
        using ZipFile zip = ZipFile.Read(FullPath);

        await Task.Run(() => zip.ExtractAll(extractPath, ExtractExistingFileAction.DoNotOverwrite), token).WaitAsync(token);
        _files = zip.EntryFileNames
            .Select(file => file.TrimEnd('/'))
            .Where(file => !file.Contains('/'))
            .Select(file => Path.Combine(extractPath, file))
            .ToArray();
    }

    protected override ClipboardMetaInfomation CreateMetaInformation()
    {
        ArgumentNullException.ThrowIfNull(_files);
        return new ClipboardMetaInfomation() { Files = _files };
    }

    protected override void SetNotification(INotification notification)
    {
        ArgumentNullException.ThrowIfNull(_files);

        notification.Title = I18n.Strings.ClipboardFileUpdated;
        notification.Message = ShowcaseText();
        var actions = ProfileActionBuilder.Build(this);
        notification.Buttons = ProfileActionBuilder.ToActionButtons(actions);
        notification.Show();
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

    public override bool IsAvailableAfterFilter()
    {
        bool hasItem = _files?.FirstOrDefault(name => Directory.Exists(name) || IsFileAvailableAfterFilter(name)) != null;
        return hasItem && !Oversized() && Config.GetConfig<SyncConfig>().EnableUploadMultiFile;
    }

    public override async Task CheckDownloadedData(CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(FullPath);

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

        var hash = await CaclHashAsync(_files, false, token);
        if (hash != Hash)
        {
            var errorMsg = $"Group data hash mismatch. Expected: {Hash}, Actual: {hash}";
            throw new InvalidDataException(errorMsg);
        }
    }

    public override HistoryRecord CreateHistoryRecord()
    {
        return new HistoryRecord
        {
            Type = ProfileType.Group,
            Text = string.Join('\n', _files ?? []),
            FilePath = _files ?? [],
            Hash = Hash,
        };
    }
}
