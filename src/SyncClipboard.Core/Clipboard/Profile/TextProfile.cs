using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract;
using SyncClipboard.Abstract.Notification;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;

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
        if (Text.Length >= 4 && (Text[..4] == "http" || Text[..4] == "www."))
        {
            void callbacker() => Sys.OpenWithDefaultApp(Text);
            notification.SendText(I18n.Strings.ClipboardTextUpdated, Text, DefaultButton(), new Button(I18n.Strings.OpenInBrowser, callbacker));
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
}
