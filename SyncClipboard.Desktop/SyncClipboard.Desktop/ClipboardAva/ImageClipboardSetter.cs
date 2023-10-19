using Avalonia.Input;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System;

namespace SyncClipboard.Desktop.ClipboardAva;

internal class ImageClipboardSetter : FileClipboardSetter, IClipboardSetter<Core.Clipboard.ImageProfile>
{
    public override object CreateClipboardObjectContainer(ClipboardMetaInfomation metaInfomation)
    {
        if (metaInfomation.Files is null || metaInfomation.Files.Length == 0)
        {
            throw new ArgumentException("Not Contain File.");
        }

        var dataObject = new DataObject();
        dataObject.Set("Text", metaInfomation?.Text ?? "");
        return dataObject;
    }
}
