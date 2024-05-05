using Ionic.Zip;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities;
using System.Text;

namespace SyncClipboard.Core.Clipboard;

public class GroupProfile : FileProfile
{
    private readonly string[] _files;

    protected override IClipboardSetter<Profile> ClipboardSetter
        => ServiceProvider.GetRequiredService<IClipboardSetter<GroupProfile>>();

    public GroupProfile(string[] files) : base()
    {
        _files = files;
    }

    public override async Task UploadProfile(IWebDav webdav, CancellationToken token)
    {
        await PrepareTransferFile(token);
        await base.UploadProfile(webdav, token);
    }

    protected async Task PrepareTransferFile(CancellationToken token)
    {
        var filePath = Path.Combine(LocalTemplateFolder, $"{Path.GetRandomFileName()}.zip");

        using ZipFile zip = new ZipFile();
        zip.AlternateEncoding = Encoding.UTF8;
        zip.AlternateEncodingUsage = ZipOption.Always;
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
}
