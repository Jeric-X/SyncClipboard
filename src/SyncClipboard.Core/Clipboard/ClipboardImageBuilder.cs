namespace SyncClipboard.Core.Clipboard;

public static class ClipboardImageBuilder
{
    public static string GetClipboardHtml(string path)
    {
        var uri = new Uri(path);
        string html = $@"<img src=""{uri}"">";
        if (OperatingSystem.IsWindows())
        {
            return ClipboardHtmlBuilder.GetClipboardHtml(html);
        }
        return html;
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