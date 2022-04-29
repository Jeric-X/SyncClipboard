using System;
using Microsoft.Toolkit.Uwp.Notifications;
#nullable enable

namespace SyncClipboard.Utility.Notification
{
    public static class Toast
    {
        public static void SendText(string title, string text, Action<string>? eventHandler = null)
        {
            new ToastContentBuilder()
                .AddArgument(text, eventHandler)
                .AddText(title)
                .AddText(text, null, null, 2)
                .Show();
        }

        public static void SendImage(string title, string text, Uri uri, Action<string>? eventHandler = null)
        {
            new ToastContentBuilder()
                .AddHeroImage(uri, "alternateText")
                .AddArgument(text, eventHandler)
                .AddText(title)
                .AddText(text, null, null, 2)
                .Show();
        }
    }
}