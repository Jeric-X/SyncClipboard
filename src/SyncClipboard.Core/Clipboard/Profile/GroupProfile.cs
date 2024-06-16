using Ionic.Zip;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract;
using SyncClipboard.Abstract.Notification;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using System.Text;

namespace SyncClipboard.Core.Clipboard;

public class GroupProfile : FileProfile
{
    private string[]? _files;

    public override ProfileType Type => ProfileType.Group;

    protected override IClipboardSetter<Profile> ClipboardSetter
        => ServiceProvider.GetRequiredService<IClipboardSetter<GroupProfile>>();

    private GroupProfile(IEnumerable<string> files, string hash)
        : base(Path.Combine(LocalTemplateFolder, $"{Path.GetRandomFileName()}.zip"), hash)
    {
        _files = files.ToArray();
    }

    public GroupProfile(ClipboardProfileDTO profileDTO) : base(profileDTO)
    {
    }

    public static async Task<GroupProfile> Create(string[] files, CancellationToken token)
    {
        var hash = await Task.Run(() => CaclHash(files)).WaitAsync(token);
        return new GroupProfile(files, hash);
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

    private static string CaclHash(string[] files)
    {
        var maxSize = Config.GetConfig<SyncConfig>().MaxFileByte;
        Array.Sort(files, FileNameCompare);
        long sumSize = 0;
        int hash = 0;
        foreach (var file in files)
        {
            if (Directory.Exists(file))
            {
                var directoryInfo = new DirectoryInfo(file);
                hash = (hash * -1521134295) + directoryInfo.Name.ListHashCode();
                var subFiles = directoryInfo.GetFiles("*", SearchOption.AllDirectories);
                Array.Sort(subFiles, FileCompare);
                foreach (var subFile in subFiles)
                {
                    sumSize += subFile.Length;
                    if (sumSize > maxSize)
                        return MD5_FOR_OVERSIZED_FILE;
                    hash = (hash * -1521134295) + (subFile.Name + subFile.Length.ToString()).ListHashCode();
                }
            }
            else if (File.Exists(file))
            {
                var fileInfo = new FileInfo(file);
                sumSize += fileInfo.Length;
                hash = (hash * -1521134295) + (fileInfo.Name + fileInfo.Length.ToString()).ListHashCode();
            }

            if (sumSize > maxSize)
            {
                return MD5_FOR_OVERSIZED_FILE;
            }
        }

        return hash.ToString();
    }

    public override async Task UploadProfile(IWebDav webdav, CancellationToken token)
    {
        await PrepareTransferFile(token);
        await base.UploadProfile(webdav, token);
    }

    public async Task PrepareTransferFile(CancellationToken token)
    {
        var filePath = Path.Combine(LocalTemplateFolder, FileName);

        using ZipFile zip = new ZipFile();
        zip.AlternateEncoding = Encoding.UTF8;
        zip.AlternateEncodingUsage = ZipOption.AsNecessary;

        ArgumentNullException.ThrowIfNull(_files);
        _files.ForEach(path =>
        {
            if (Directory.Exists(path))
            {
                zip.AddDirectory(path, Path.GetFileName(path));
            }
            else if (File.Exists(path))
            {
                zip.AddItem(path, "");
            }
        });

        await Task.Run(() => zip.Save(filePath), token).WaitAsync(token);
        FullPath = filePath;
    }

    public override async Task BeforeSetLocal(CancellationToken token, IProgress<HttpDownloadProgress>? progress)
    {
        await base.BeforeSetLocal(token, progress);
        await ExtractFiles(token);
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

    protected override Task CheckHash(string _, CancellationToken _1) => Task.CompletedTask;

    protected override void SetNotification(INotification notification)
    {
        ArgumentNullException.ThrowIfNull(_files);
        ArgumentNullException.ThrowIfNull(FullPath);
        notification.SendText(
            I18n.Strings.ClipboardFileUpdated,
            ShowcaseText(),
            DefaultButton()
#if WINDOWS
            , new Button(I18n.Strings.OpenFolder, () => Sys.OpenFolderInExplorer(FullPath[..^4] + "\\"))
#endif
        );
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
}
