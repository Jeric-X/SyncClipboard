using Microsoft.Toolkit.Uwp.Notifications;
using SyncClipboard.Abstract.Notification;

namespace SyncClipboard.Windows.Notification
{
    static class ToastContentBuilderExtend
    {
        public static ToastContentBuilder AddButton(
            this ToastContentBuilder content, Button button)
        {
            return content.AddButton(
                new ToastButton(button.Text, button.Uid)
                {
                    ActivationType = ToastActivationType.Background
                }
            );
        }
    }
}