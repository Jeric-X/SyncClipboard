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

        // 支持新的 DataTransfer 和旧的 DataObject
        if (package is DataTransfer dataTransfer)
        {
            await SetFilesToDataTransfer(dataTransfer, metaInfomation.Files);
        }
        else if (package is DataObject dataObject)
        {
            if (OperatingSystem.IsLinux())
            {
                SetLinux(dataObject, metaInfomation.Files);
            }
            else if (OperatingSystem.IsMacOS())
            {
                await SetMacos(dataObject, metaInfomation.Files);
            }
        }
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

        foreach (var item in storageItems.Where(item => item is not null))
        {
            dataTransfer.Add(DataTransferItem.Create(DataFormat.File, item!));
        }
    }

    [SupportedOSPlatform("linux")]
    private static void SetLinux(DataObject dataObject, string[] files)
    {
        dataObject.Set(Format.TEXT, Encoding.UTF8.GetBytes(string.Join('\n', files)));

        var uriEnum = files.Select(file => new Uri(file).GetComponents(UriComponents.SerializationInfoString, UriFormat.UriEscaped));
        var uris = string.Join("\n", uriEnum);

        dataObject.Set(Format.UriList, Encoding.UTF8.GetBytes(uris));

        var nautilus = $"x-special/nautilus-clipboard\ncopy\n{uris}\n";
        dataObject.Set(Format.CompoundText, Encoding.UTF8.GetBytes(nautilus));

        var gnome = $"copy\n{uris}";
        dataObject.Set(Format.GnomeFiles, Encoding.UTF8.GetBytes(gnome));
    }

    [SupportedOSPlatform("macos")]
    private static async Task SetMacos(DataObject dataObject, string[] files)
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

        dataObject.Set(Format.FileList, storageItems.Where(item => item is not null));
    }
}
