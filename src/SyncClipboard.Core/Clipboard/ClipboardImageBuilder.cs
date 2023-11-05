using System.Runtime.Versioning;

namespace SyncClipboard.Core.Clipboard;

public static class ClipboardImageBuilder
{
    [SupportedOSPlatform("windows")]
    public static string GetClipboardHtml(string path)
    {
        string html = $@"<img src=""file:///{path}"">";
        return ClipboardHtmlBuilder.GetClipboardHtml(html);
    }

    private const string clipboardQqFormat = @"<QQRichEditFormat>
<Info version=""1001"">
</Info>
<EditElement type=""1"" imagebiztype=""0"" textsummary="""" filepath=""<<<<<<"" shortcut="""">
</EditElement>
</QQRichEditFormat>";

    public static string GetClipboardQQFormat(string path)
    {
        return clipboardQqFormat.Replace("<<<<<<", path);
    }
}

