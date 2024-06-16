using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract;
using SyncClipboard.Abstract.Notification;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;
using System.Text.RegularExpressions;

namespace SyncClipboard.Core.Clipboard;

public class TextProfile : Profile
{
    public override ProfileType Type => ProfileType.Text;

    protected override IClipboardSetter<Profile> ClipboardSetter
        => ServiceProvider.GetRequiredService<IClipboardSetter<TextProfile>>();

    public TextProfile(string text)
    {
        Text = text;
    }

    public override string ToolTip()
    {
        return Text;
    }

    public override string ShowcaseText()
    {
        if (Text.Length > 500)
        {
            return Text[..500] + "\n...";
        }
        return Text;
    }

    protected override bool Same(Profile rhs)
    {
        try
        {
            var textprofile = (TextProfile)rhs;
            return Text == textprofile.Text;
        }
        catch
        {
            return false;
        }
    }

    public override async Task UploadProfile(IWebDav webdav, CancellationToken cancelToken)
    {
        await webdav.PutJson(RemoteProfilePath, ToDto(), cancelToken);
    }

    protected override void SetNotification(INotification notification)
    {
        if (Text.Length >= 4 && HasUrl(Text, out var url))
        {
            var button = new Button(I18n.Strings.OpenInBrowser, () => Sys.OpenWithDefaultApp(url));
            notification.SendText(I18n.Strings.ClipboardTextUpdated, Text, DefaultButton(), button);
        }
        else
        {
            notification.SendText(I18n.Strings.ClipboardTextUpdated, Text, DefaultButton());
        }
    }

    protected override ClipboardMetaInfomation CreateMetaInformation()
    {
        return new ClipboardMetaInfomation { Text = Text };
    }

    private static bool HasUrl(string str, out string? url)
    {
        url = null;
        var expression = @"([^a-zA-Z0-9]|^)(?'url'https?://(?'domain'[a-zA-Z0-9.\-_]*)?(?>:(?'port'\d{1,5}))?(/(?'path'[a-zA-Z0-9._\-%]+))*(?:(?>\?(?'query'[a-zA-Z0-9._\-=&%]+))()|(?>#(?'anchor'[a-zA-Z0-9._\-%]+))()){0,2})";
        var match = Regex.Match(str, expression);
        if (match.Success)
        {
            var matchedUrl = match.Groups["url"].Value;
            if (Uri.IsWellFormedUriString(matchedUrl, UriKind.Absolute))
            {
                url = matchedUrl;
                return true;
            }
        }
        return false;
    }
}
