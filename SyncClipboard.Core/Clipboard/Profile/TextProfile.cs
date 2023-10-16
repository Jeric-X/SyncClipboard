using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Notification;
using Button = SyncClipboard.Core.Utilities.Notification.Button;

namespace SyncClipboard.Core.Clipboard;

public class TextProfile : Profile
{
    public override ProfileType Type => ProfileType.Text;

    protected override IClipboardSetter<Profile> ClipboardSetter { get; set; }
    protected override IServiceProvider ServiceProvider { get; set; }

    public TextProfile(string text, IServiceProvider serviceProvider)
    {
        Text = text;
        ClipboardSetter = serviceProvider.GetRequiredService<IClipboardSetter<TextProfile>>();
        ServiceProvider = serviceProvider;
    }

    public override string ToolTip()
    {
        return Text;
    }

    protected override Task<bool> Same(Profile rhs, CancellationToken cancellationToken)
    {
        try
        {
            var textprofile = (TextProfile)rhs;
            return Task.FromResult(Text == textprofile.Text);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public override async Task UploadProfile(IWebDav webdav, CancellationToken cancelToken)
    {
        await webdav.PutText(RemoteProfilePath, this.ToJsonString(), cancelToken);
    }

    protected override void SetNotification(NotificationManager notification)
    {
        if (Text.Length >= 4 && (Text[..4] == "http" || Text[..4] == "www."))
        {
            Callbacker callbacker = new(Guid.NewGuid().ToString(), (_) => Sys.OpenWithDefaultApp(Text));
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
