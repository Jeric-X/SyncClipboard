using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System;
using System.IO;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace SyncClipboard.WinUI3.ClipboardWinUI;

internal class GroupClipboardSetter : ClipboardSetterBase<GroupProfile>
{
    protected override DataPackage CreatePackage(ClipboardMetaInfomation metaInfomation)
    {
        if (metaInfomation.Files is null || metaInfomation.Files.Length == 0)
        {
            throw new ArgumentException("Not Contain File.");
        }

        var items = metaInfomation.Files
            .Where(file => Directory.Exists(file) || File.Exists(file))
            .Select<string, IStorageItem>(file =>
            {
                if (Directory.Exists(file))
                {
                    return StorageFolder.GetFolderFromPathAsync(file).AsTask().Result;
                }
                return StorageFile.GetFileFromPathAsync(file).AsTask().Result;
            });

        var dataObject = new DataPackage();
        dataObject.SetStorageItems(items.ToArray());
        return dataObject;
    }
}
