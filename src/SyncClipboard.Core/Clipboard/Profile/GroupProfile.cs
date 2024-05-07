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

    private GroupProfile(string[] files, string hash)
        : base(Path.Combine(LocalTemplateFolder, $"{Path.GetRandomFileName()}.zip"), hash)
    {
        _files = files;
    }

    public GroupProfile(ClipboardProfileDTO profileDTO) : base(profileDTO)
    {
    }

    public static async Task<GroupProfile> Create(string[] files, CancellationToken token)
    {
        var hash = await Task.Run(() => CaclHash(files)).WaitAsync(token);
        return new GroupProfile(files, hash);
    }

    private static string CaclHash(string[] files)
    {
        var maxSize = Config.GetConfig<SyncConfig>().MaxFileByte;
        Array.Sort(files);
        long sumSize = 0;
        int hash = 0;
        string? hashString = null;
        foreach (var file in files)
        {
            if (Directory.Exists(file))
            {
                var directoryInfo = new DirectoryInfo(file);
                hash = (hash * -1521134295) + directoryInfo.Name.ListHashCode();
                foreach (var subFile in directoryInfo.GetFiles("*", SearchOption.AllDirectories))
                {
                    sumSize += subFile.Length;
                    if (sumSize > maxSize)
                        break;
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
                hashString = MD5_FOR_OVERSIZED_FILE;
                break;
            }
        }

        return hashString ?? hash.ToString();
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
            string.Join("\n", _files.Select(file => Path.GetFileName(file))),
            DefaultButton()
#if WINDOWS
            , new Button(I18n.Strings.OpenFolder, () => Sys.OpenFolderInExplorer(FullPath[..^4] + "\\"))
#endif
        );
    }
}
