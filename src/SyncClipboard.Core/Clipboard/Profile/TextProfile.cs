using Microsoft.Extensions.DependencyInjection;
using NativeNotification.Interface;
using SyncClipboard.Abstract;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace SyncClipboard.Core.Clipboard;

public class TextProfile : Profile
{
    public override ProfileType Type => ProfileType.Text;

    protected override IClipboardSetter<Profile> ClipboardSetter
        => ServiceProvider.GetRequiredService<IClipboardSetter<TextProfile>>();

    public TextProfile(string text, bool contentControl = true)
    {
        Text = text;
        ContentControl = contentControl;
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

    protected override void SetNotification(INotification notification)
    {
        notification.Title = I18n.Strings.ClipboardTextUpdated;
        notification.Message = Text;
        if (Text.Length >= 4 && HasUrl(Text, out var url))
        {
            notification.Buttons = [new ActionButton(I18n.Strings.OpenInBrowser, () => Sys.OpenWithDefaultApp(url)), DefaultButton()];
        }
        else
        {
            notification.Buttons = [DefaultButton()];
        }
        notification.Show();
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

    public override bool IsAvailableAfterFilter() => Config.GetConfig<SyncConfig>().EnableUploadText;

    public override HistoryRecord CreateHistoryRecord()
    {
        byte[] inputBytes = System.Text.Encoding.Unicode.GetBytes(Text);
        byte[] hashBytes = MD5.HashData(inputBytes);
        var hash = Convert.ToHexString(hashBytes);
        return new HistoryRecord
        {
            Type = ProfileType.Text,
            Hash = hash,
            Text = Text,
        };
    }
}
