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

    [SupportedOSPlatform("macos")] public const string FileList = "public.file-url";
    [SupportedOSPlatform("macos")] public const string PublicPng = "public.png";
    [SupportedOSPlatform("macos")] public const string PublicTiff = "public.tiff";
    [SupportedOSPlatform("macos")] public const string PublicHtml = "public.html";
}
