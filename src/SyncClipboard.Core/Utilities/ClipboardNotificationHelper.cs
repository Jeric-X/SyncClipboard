using NativeNotification.Interface;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Utilities;

public class ProfileNotificationHelper(
    INotificationManager notificationManager, ProfileActionBuilder profileActionBuilder, ConfigManager configManager)
{
    private INotification? _notification;

    public async void Notify(Profile profile, CancellationToken token = default)
    {
        _notification?.Remove();
        // Keep expiration callbacks scoped to their own notification, never a newer notification.
        var notification = notificationManager.Create();
        _notification = notification;

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

        notification.Message = profile.ShortDisplayText;
        var actions = await profileActionBuilder.Build(profile, token);
        notification.Buttons = ProfileActionBuilder.ToActionButtons(actions);
        notification.ContentAction = (await profileActionBuilder.GetPrimaryAction(profile, token))?.Action;
        var durationSeconds = configManager.GetConfig<SyncConfig>().NotificationDurationSeconds;
        notification.Show(durationSeconds > 0
            ? new NotificationDeliverOption { Duration = TimeSpan.FromSeconds(durationSeconds) }
            : null);
    }
}
