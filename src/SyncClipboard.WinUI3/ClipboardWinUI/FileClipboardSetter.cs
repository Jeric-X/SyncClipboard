using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace SyncClipboard.WinUI3.ClipboardWinUI;

internal class FileClipboardSetter : ClipboardSetterBase<FileProfile>
{
    public override object CreateClipboardObjectContainer(ClipboardMetaInfomation metaInfomation)
    {
        if (metaInfomation.Files is null || metaInfomation.Files.Length == 0)
        {
            throw new ArgumentException("Not Contain File.");
        }

        var dataObject = new DataPackage();
        var storageFile = StorageFile.GetFileFromPathAsync(metaInfomation.Files[0]).AsTask().Result;
        dataObject.SetStorageItems(new[] { storageFile });
        return dataObject;
    }
}
