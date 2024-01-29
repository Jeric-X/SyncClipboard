using Microsoft.Toolkit.Uwp.Notifications;
using SyncClipboard.Abstract;

namespace SyncClipboard.Core.Utilities.Notification
{
    static class ToastContentBuilderExtend
    {
        public static ToastContentBuilder AddButton(
            this ToastContentBuilder content, Button button)
        {
            if (button.Callbacker is not null)
            {
                Handler.AddHandler(button.Uid.ToString(), (_) => button.Callbacker());
            }
            return content.AddButton(
                new ToastButton(button.Text, button.Uid)
                {
                    ActivationType = ToastActivationType.Background
                }
            );
        }
    }
}