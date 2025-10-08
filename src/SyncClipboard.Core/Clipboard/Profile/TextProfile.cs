using Microsoft.Extensions.DependencyInjection;
using NativeNotification.Interface;
using SyncClipboard.Abstract;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using System.Security.Cryptography;

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
        var actions = ProfileActionBuilder.Build(this);
        notification.Buttons = ProfileActionBuilder.ToActionButtons(actions);
        notification.Show();
    }

    protected override ClipboardMetaInfomation CreateMetaInformation()
    {
        return new ClipboardMetaInfomation { Text = Text };
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
