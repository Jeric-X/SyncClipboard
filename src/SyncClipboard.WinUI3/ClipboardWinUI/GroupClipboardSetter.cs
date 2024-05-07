using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
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

        List<IStorageItem> list = new();
        foreach (var file in metaInfomation.Files)
        {
            if (Directory.Exists(file))
            {
                list.Add(StorageFolder.GetFolderFromPathAsync(file).AsTask().Result);
            }
            else if (FileClipboardSetter.IsUnusualType(file))
            {
                list.Add(new UnusualStorageItem(file));
            }
            else
            {
                list.Add(StorageFile.GetFileFromPathAsync(file).AsTask().Result);
            }
        }

        var dataObject = new DataPackage();
        dataObject.SetStorageItems(list, false);
        return dataObject;
    }
}
