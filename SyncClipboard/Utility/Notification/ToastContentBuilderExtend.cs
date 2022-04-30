using System;
using Microsoft.Toolkit.Uwp.Notifications;
#nullable enable

namespace SyncClipboard.Utility.Notification
{
    static class ToastContentBuilderExtend
    {
        public static ToastContentBuilder AddButton(
            this ToastContentBuilder content, Button button)
        {
            if (button.Callbacker is not null)
            {
                Handler.AddHandler(button.Callbacker.Argument, button.Callbacker.CallBack);
            }
            return content.AddButton(
                new ToastButton(button.Text, button.Callbacker?.Argument)
                {
                    ActivationType = ToastActivationType.Background,
                    ActivationOptions = new ToastActivationOptions()
                    {
                        AfterActivationBehavior =
                            button.Callbacker?.Pedding ?? false ?
                                  ToastAfterActivationBehavior.PendingUpdate : ToastAfterActivationBehavior.Default
                    }
                }
            );
        }
    }
}