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
        else if (OperatingSystem.IsMacOS())
        {
            SetMacos(dataObject, metaInfomation.Files[0]);
        }

        return dataObject;
    }

    [SupportedOSPlatform("linux")]
    private static void SetLinux(DataObject dataObject, string path)
    {
        dataObject.Set(Format.Text, Encoding.UTF8.GetBytes(path));

        var uri = new Uri(path);
        var uriPath = uri.GetComponents(UriComponents.SerializationInfoString, UriFormat.UriEscaped);
        dataObject.Set(Format.UriList, Encoding.UTF8.GetBytes(uriPath));

        var nautilus = $"x-special/nautilus-clipboard\ncopy\n{uriPath}\n";
        var nautilusBytes = Encoding.UTF8.GetBytes(nautilus);
        dataObject.Set(Format.CompoundText, nautilusBytes);
    }

    [SupportedOSPlatform("macos")]
    private static void SetMacos(DataObject dataObject, string path)
    {
        dataObject.Set("public.utf8-plain-text", Encoding.UTF8.GetBytes(path));

        var uri = new Uri(path);
        var uriPath = uri.GetComponents(UriComponents.SerializationInfoString, UriFormat.UriEscaped);
        dataObject.Set(Format.FileList, Encoding.UTF8.GetBytes(uriPath));
    }
}
