using Microsoft.Extensions.DependencyInjection;
using NativeNotification.Interface;
using SyncClipboard.Core.Clipboard;

namespace SyncClipboard.Core.Utilities;

public class ProfileNotificationHelper([FromKeyedServices("ProfileNotification")] INotification notification, ProfileActionBuilder profileActionBuilder)
{
    public void Notify(Profile profile)
    {
        ResetNotification(notification);

        notification.Title = profile switch
        {
            TextProfile => I18n.Strings.ClipboardTextUpdated,
            ImageProfile => I18n.Strings.ClipboardImageUpdated,
            GroupProfile => I18n.Strings.ClipboardFileUpdated,
            FileProfile => I18n.Strings.ClipboardFileUpdated,
            _ => I18n.Strings.ClipboardUpdated,
        };

        if (profile is ImageProfile imageProfile && File.Exists(imageProfile.FullPath))
        {
            notification.Image = new Uri(imageProfile.FullPath);
        }

        notification.Message = profile.GetDisplayText();
        var actions = profileActionBuilder.Build(profile);
        notification.Buttons = ProfileActionBuilder.ToActionButtons(actions);
        notification.Show();
    }

    private static void ResetNotification(INotification notification)
    {
        notification.Title = string.Empty;
        notification.Message = string.Empty;
        notification.Image = null;
        notification.Buttons = [];
        notification.ContentAction = null;
        notification.Remove();
    }
}
