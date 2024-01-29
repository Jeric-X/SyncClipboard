using Microsoft.Toolkit.Uwp.Notifications;
using SyncClipboard.Abstract;
using SyncClipboard.Abstract.Notification;
using Windows.UI.Notifications;

namespace SyncClipboard.Core.Utilities.Notification
{
    public class NotificationManager : IDisposable, INotification
    {
        public readonly ToastNotifier Notifer;
        private readonly CallbackHandler<string> _callbackHandler = new();

        public NotificationManager()
        {
            Notifer = ToastNotificationManager.CreateToastNotifier(Register.RegistFromCurrentProcess());

            // 像系统注册通知按钮回调，如果不注册，系统会打开新的进程；真正的回调在ToastSession中
            ToastNotificationManagerCompat.OnActivated += (args) => { };
        }

        ~NotificationManager() => Dispose();

        public void SendText(string title, string text, params Button[] buttons)
        {
            new ToastSession(title, Notifer, _callbackHandler) { Text1 = text, Buttons = new(buttons) }.Show();
        }

        public void SendImage(string title, string text, Uri uri, params Button[] buttons)
        {
            new ToastSession(title, Notifer, _callbackHandler)
            {
                Text1 = text,
                Buttons = new(buttons),
                Image = uri
            }.Show();
        }

        public IProgressBar CreateProgressNotification(string title)
        {
            return new ProgressBar(title, Notifer, _callbackHandler);
        }

        public void Dispose()
        {
            Register.UnRegistFromCurrentProcess();
            GC.SuppressFinalize(this);
        }
    }
}