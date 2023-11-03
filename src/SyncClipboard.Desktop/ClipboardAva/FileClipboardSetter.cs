using Avalonia.Input;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System;
using System.Runtime.Versioning;
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
        if (OperatingSystem.IsLinux())
        {
            SetLinux(dataObject, metaInfomation.Files[0]);
        }

        return dataObject;
    }

    [SupportedOSPlatform("linux")]
    private static void SetLinux(DataObject dataObject, string path)
    {
        var uriPath = new Uri(path).GetComponents(UriComponents.SerializationInfoString, UriFormat.UriEscaped);
        dataObject.Set(Format.UriList, Encoding.UTF8.GetBytes(uriPath));

        var nautilus = $"x-special/nautilus-clipboard\ncopy\n{uriPath}\n";
        var nautilusBytes = Encoding.UTF8.GetBytes(nautilus);
        dataObject.Set("COMPOUND_TEXT", nautilusBytes);
        dataObject.Set("TEXT", nautilusBytes);
    }
}
