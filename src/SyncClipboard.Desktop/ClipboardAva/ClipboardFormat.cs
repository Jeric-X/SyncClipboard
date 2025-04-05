using Avalonia.Input;
using System.Runtime.Versioning;

namespace SyncClipboard.Desktop.ClipboardAva;

internal static class Format
{
    [SupportedOSPlatform("linux")] public const string UriList = "text/uri-list";
    [SupportedOSPlatform("linux")] public const string GnomeFiles = "x-special/gnome-copied-files";
    [SupportedOSPlatform("linux")] public const string ImagePng = "image/png";
    [SupportedOSPlatform("linux")] public const string ImageJpeg = "image/jpeg";
    [SupportedOSPlatform("linux")] public const string ImageBmp = "image/bmp";
    [SupportedOSPlatform("linux")] public const string TimeStamp = "TIMESTAMP";
    [SupportedOSPlatform("linux")] public const string Text = "TEXT";
    [SupportedOSPlatform("linux")] public const string TextUtf8 = "text/plain;charset=utf-8";
    [SupportedOSPlatform("linux")] public const string CompoundText = "COMPOUND_TEXT";
    [SupportedOSPlatform("linux")] public const string KdeCutSelection = "application/x-kde-cutselection";
    [SupportedOSPlatform("linux")] public const string TextHtml = "text/html";

    [SupportedOSPlatform("macos")] public readonly static string FileList = DataFormats.Files;
    [SupportedOSPlatform("macos")] public const string PublicPng = "public.png";
    [SupportedOSPlatform("macos")] public const string PublicTiff = "public.tiff";
    [SupportedOSPlatform("macos")] public const string PublicHtml = "public.html";
    [SupportedOSPlatform("macos")] public const string MacText = "Text";
    [SupportedOSPlatform("macos")] public const string NSPasteboardTransient = "org.nspasteboard.TransientType";
    [SupportedOSPlatform("macos")] public const string NSpasteboardConcealed = "org.nspasteboard.ConcealedType";
}
