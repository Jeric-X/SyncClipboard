using System;
using Microsoft.Toolkit.Uwp.Notifications;
#nullable enable

namespace SyncClipboard.Utility.Notification
{
    static class ToastContentBuilderExtend
    {
        public static ToastContentBuilder AddButton(
            this ToastContentBuilder content, ToastButton button, string arg, Action<string> callBack)
        {
            Handler.AddHandler(arg, callBack);
            return content.AddButton(button.AddArgument("", arg));
        }

        public static ToastContentBuilder AddArgument(
            this ToastContentBuilder content, string arg, Action<string>? callBack)
        {
            if (callBack is not null)
            {
                Handler.AddHandler(arg, callBack);
            }
            return content.AddArgument("", arg);
        }
    }
}