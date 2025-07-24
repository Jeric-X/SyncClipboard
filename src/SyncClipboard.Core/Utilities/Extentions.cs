using NativeNotification.Interface;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Utilities;

public static class Extentions
{
    public static LocaleString<T> Match<T>(this IEnumerable<LocaleString<T>> localeStrings, T obj)
    {
        if (!localeStrings.Any())
        {
            throw new ArgumentNullException(nameof(localeStrings));
        }
        return localeStrings.FirstOrDefault(x => EqualityComparer<T>.Default.Equals(x.Key, obj)) ?? localeStrings.First();
    }

    public static INotification ShowText(this INotificationManager notificationManager, string title, string message, IEnumerable<ActionButton>? buttons = null)
    {
        var notification = notificationManager.Create();
        notification.Title = title;
        notification.Message = message;
        if (buttons != null)
        {
            notification.Buttons = buttons.ToList();
        }
        notification.Show();
        return notification;
    }
}
