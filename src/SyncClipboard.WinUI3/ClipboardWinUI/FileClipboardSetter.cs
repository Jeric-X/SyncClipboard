using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System;
using System.IO;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace SyncClipboard.WinUI3.ClipboardWinUI;

internal class FileClipboardSetter : ClipboardSetterBase<FileProfile>
{
    public static string[] UnusualType = { ".lnk", ".url", ".wsh" };

    protected override DataPackage CreatePackage(ClipboardMetaInfomation metaInfomation)
    {
        if (metaInfomation.Files is null || metaInfomation.Files.Length == 0)
        {
            throw new ArgumentException("Not Contain File.");
        }

        var dataObject = new DataPackage();
        IStorageItem storageFile = IsUnusualType(metaInfomation.Files[0])
            ? new UnusualStorageItem(metaInfomation.Files[0])
                : StorageFile.GetFileFromPathAsync(metaInfomation.Files[0]).AsTask().Result;

        dataObject.SetStorageItems(new[] { storageFile }, false);
        return dataObject;
    }

    public static bool IsUnusualType(string file)
    {
        var exention = Path.GetExtension(file).ToLower();
        foreach (var type in UnusualType)
        {
            if (exention == type)
                return true;
        }
        return false;
    }
}
