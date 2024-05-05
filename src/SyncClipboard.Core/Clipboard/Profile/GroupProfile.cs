using Ionic.Zip;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;
using System.Text;

namespace SyncClipboard.Core.Clipboard;

public class GroupProfile : FileProfile
{
    private string[]? _files;

    public override ProfileType Type => ProfileType.Group;
    public override string FileName
    {
        get
        {
            if (string.IsNullOrEmpty(base.FileName))
            {
                FileName = $"{Path.GetRandomFileName()}.zip";
            }
            return base.FileName;
        }
        set => base.FileName = value;
    }

    protected override IClipboardSetter<Profile> ClipboardSetter
        => ServiceProvider.GetRequiredService<IClipboardSetter<GroupProfile>>();

    public GroupProfile(string[] files) : base()
    {
        _files = files;
    }

    public GroupProfile(ClipboardProfileDTO profileDTO) : base(profileDTO)
    {
    }

    public override async Task UploadProfile(IWebDav webdav, CancellationToken token)
    {
        await PrepareTransferFile(token);
        await base.UploadProfile(webdav, token);
    }

    protected async Task PrepareTransferFile(CancellationToken token)
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

        ArgumentNullException.ThrowIfNull(FullPath);
        var extractPath = FullPath[..^4];
        if (!Directory.Exists(extractPath))
            Directory.CreateDirectory(extractPath);

        var fileList = new List<string>();
        using ZipFile zip = ZipFile.Read(FullPath);

        await Task.Run(() => zip.ExtractAll(extractPath, ExtractExistingFileAction.DoNotOverwrite), token).WaitAsync(token);
        _files = zip.EntryFileNames.Select(fileName => Path.Combine(extractPath, fileName.TrimEnd('\\', '/'))).ToArray();
    }

    protected override ClipboardMetaInfomation CreateMetaInformation()
    {
        ArgumentNullException.ThrowIfNull(_files);
        return new ClipboardMetaInfomation() { Files = _files };
    }
}
