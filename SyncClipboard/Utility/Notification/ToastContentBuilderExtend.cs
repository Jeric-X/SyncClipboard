using System;
using Microsoft.Toolkit.Uwp.Notifications;
#nullable enable

namespace SyncClipboard.Utility.Notification
{
    static class ToastContentBuilderExtend
    {
        public static ToastContentBuilder AddButton(
            this ToastContentBuilder content, ToastButton button, Action<string> callBack)
        {
            Handler.AddHandler(button.Arguments, callBack);
            return content.AddButton(button);
        }

        public static ToastContentBuilder AddButton(
            this ToastContentBuilder content, Button button)
        {
            if (button.Callbacker is not null)
            {
                Handler.AddHandler(button.Callbacker.Argument, button.Callbacker.CallBack);
            }
            return content.AddButton(new ToastButton(button.Text, button.Callbacker?.Argument));
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