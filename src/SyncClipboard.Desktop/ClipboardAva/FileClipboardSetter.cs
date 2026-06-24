using Avalonia.Input;
using Avalonia.Platform.Storage;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva;

internal class FileClipboardSetter : ClipboardSetterBase<FileProfile>, IClipboardSetter<GroupProfile>
{
    public override async Task FillPackage(object package, ClipboardMetaInfomation metaInfomation)
    {
        if (metaInfomation.Files is null || metaInfomation.Files.Length == 0)
        {
            throw new ArgumentException("Not Contain File.");
        }

        if (package is not DataTransfer dataTransfer)
        {
            return;
        }

        await SetFilesToDataTransfer(dataTransfer, metaInfomation.Files);
    }

    private static async Task SetFilesToDataTransfer(DataTransfer dataTransfer, string[] files)
    {
        var provider = App.Current.MainWindow.StorageProvider;
        var storageItems = await Task.WhenAll(files.Select(async file =>
        {
            if (Directory.Exists(file))
            {
                return (IStorageItem?)await provider.TryGetFolderFromPathAsync(file);
            }
            return await provider.TryGetFileFromPathAsync(file);
        }));

        var validItems = storageItems.Where(item => item is not null).Cast<IStorageItem>().ToList();

        // 为每个文件创建独立的 DataTransferItem
        foreach (var storageItem in validItems)
        {
            var item = new DataTransferItem();
            item.SetFile(storageItem);
            dataTransfer.Add(item);
        }

        // Linux 特定格式：合并所有文件 URI
        if (OperatingSystem.IsLinux())
        {
            SetLinuxFormats(dataTransfer, files);
        }
    }

    [SupportedOSPlatform("linux")]
    private static void SetLinuxFormats(DataTransfer dataTransfer, string[] files)
    {
        // 创建一个额外的 item 用于 Linux 特定格式
        var item = new DataTransferItem();

        item.Set(DataFormat.CreateBytesPlatformFormat(Format.TEXT), Encoding.UTF8.GetBytes(string.Join('\n', files)));

        var uriEnum = files.Select(file => new Uri(file).GetComponents(UriComponents.SerializationInfoString, UriFormat.UriEscaped));
        var uris = string.Join("\n", uriEnum);

        item.Set(DataFormat.CreateBytesPlatformFormat(Format.UriList), Encoding.UTF8.GetBytes(uris));

        var nautilus = $"x-special/nautilus-clipboard\ncopy\n{uris}\n";
        item.Set(DataFormat.CreateBytesPlatformFormat(Format.CompoundText), Encoding.UTF8.GetBytes(nautilus));

        var gnome = $"copy\n{uris}";
        item.Set(DataFormat.CreateBytesPlatformFormat(Format.GnomeFiles), Encoding.UTF8.GetBytes(gnome));

        dataTransfer.Add(item);
    }
}
