using Avalonia.Input;
using Avalonia.Platform.Storage;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;

namespace SyncClipboard.Desktop.ClipboardAva;

internal class FileClipboardSetter : ClipboardSetterBase<FileProfile>, IClipboardSetter<GroupProfile>
{
    protected override DataObject CreatePackage(ClipboardMetaInfomation metaInfomation)
    {
        if (metaInfomation.Files is null || metaInfomation.Files.Length == 0)
        {
            throw new ArgumentException("Not Contain File.");
        }

        var dataObject = new DataObject();
        if (OperatingSystem.IsLinux())
        {
            SetLinux(dataObject, metaInfomation.Files);
        }
        else if (OperatingSystem.IsMacOS())
        {
            SetMacos(dataObject, metaInfomation.Files);
        }

        return dataObject;
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
    private static void SetMacos(DataObject dataObject, string[] files)
    {
        var provider = App.Current.MainWindow.StorageProvider;
        var storageItems = files.Select<string, IStorageItem?>(file =>
        {
            if (Directory.Exists(file))
            {
                return provider.TryGetFolderFromPathAsync(file).Result;
            }
            return provider.TryGetFileFromPathAsync(file).Result;
        }).Where(item => item is not null);

        dataObject.Set(Format.FileList, storageItems);
    }
}
