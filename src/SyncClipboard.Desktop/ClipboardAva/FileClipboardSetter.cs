using Avalonia.Input;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System;
using System.Linq;
using System.Text;

namespace SyncClipboard.Desktop.ClipboardAva;

internal class FileClipboardSetter : ClipboardSetterBase<FileProfile>
{
    public override object CreateClipboardObjectContainer(ClipboardMetaInfomation metaInfomation)
    {
        if (metaInfomation.Files is null || metaInfomation.Files.Length == 0)
        {
            throw new ArgumentException("Not Contain File.");
        }

        var dataObject = new DataObject();
        var urlList = string.Join("\n", metaInfomation.Files.Select(x => new Uri(x).ToString()));
        dataObject.Set(Format.UriList, Encoding.UTF8.GetBytes(urlList));
        return dataObject;
    }
}
