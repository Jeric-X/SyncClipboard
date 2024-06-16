using AppKit;
using Foundation;
using SyncClipboard.Abstract.Notification;
using SyncClipboard.Desktop.Utilities;
using System;

namespace SyncClipboard.Desktop.MacOS.Utilities;

internal class Notification : INotification
{
    public IProgressBar CreateProgressNotification(string title)
    {
        return new ProgressBar();
    }

    public void SendImage(string title, string text, Uri uri, params Button[] buttons)
    {
        using var notification = new NSUserNotification
        {
            Title = title,
            InformativeText = text,
            SoundName = NSUserNotification.NSUserNotificationDefaultSoundName,
            ContentImage = new NSImage(uri!)
        };

        NSUserNotificationCenter.DefaultUserNotificationCenter.DeliverNotification(notification);
    }

    public void SendText(string title, string text, params Button[] buttons)
    {
        using var notification = new NSUserNotification
        {
            Title = title,
            InformativeText = text,
            SoundName = NSUserNotification.NSUserNotificationDefaultSoundName,
        };

        NSUserNotificationCenter.DefaultUserNotificationCenter.DeliverNotification(notification);
    }

    public void SendTemporary(NotificationPara para)
    {
        using var notification = new NSUserNotification
        {
            Title = para.Title,
            InformativeText = para.Text,
            SoundName = NSUserNotification.NSUserNotificationDefaultSoundName
        };

        if (para.Image is not null)
        {
            notification.ContentImage = new NSImage(para.Image!);
        }

        NSUserNotificationCenter.DefaultUserNotificationCenter.DeliverNotification(notification);
    }
}