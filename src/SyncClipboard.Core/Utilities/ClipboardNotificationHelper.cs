using Microsoft.Extensions.DependencyInjection;
using NativeNotification.Interface;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Utilities;

public class ProfileNotificationHelper(
    [FromKeyedServices("ProfileNotification")] INotification notification,
    ProfileActionBuilder profileActionBuilder, ConfigManager configManager)
{
    private readonly Lock _notificationLock = new();
    private long _version;

    public void Notify(Profile profile, CancellationToken token = default)
    {
        DelegateExtention.SafeFireAndForget(() => NotifyAsync(profile, token), nameof(ProfileNotificationHelper));
    }

    internal async Task NotifyAsync(Profile profile, CancellationToken token = default)
    {
        long version;
        lock (_notificationLock)
        {
            version = ++_version;
            ResetNotification(notification);
        }

        var actions = await profileActionBuilder.Build(profile, token);
        var primaryAction = await profileActionBuilder.GetPrimaryAction(profile, token);
        lock (_notificationLock)
        {
            if (version != _version || token.IsCancellationRequested)
            {
                return;
            }

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
            notification.Buttons = ProfileActionBuilder.ToActionButtons(actions);
            notification.ContentAction = primaryAction?.Action;
            var durationSeconds = configManager.GetConfig<SyncConfig>().NotificationDurationSeconds;
            notification.Show(new NotificationDeliverOption
            {
                Duration = durationSeconds > 0 ? TimeSpan.FromSeconds(durationSeconds) : null
            });
        }
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
