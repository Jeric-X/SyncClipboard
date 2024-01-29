using SyncClipboard.Abstract;
using Windows.UI.Notifications;

namespace SyncClipboard.Core.Utilities.Notification
{
    public class NotificationManager : IDisposable, INotification
    {
        public readonly ToastNotifier Notifer;

        public NotificationManager()
        {
            Notifer = ToastNotificationManager.CreateToastNotifier(Register.RegistFromCurrentProcess());
        }

        ~NotificationManager() => Dispose();

        public void SendText(string title, string text, params Button[] buttons)
        {
            new ToastSession(title, Notifer) { Text1 = text, Buttons = new(buttons) }.Show();
        }

        public void SendImage(string title, string text, Uri uri, params Button[] buttons)
        {
            new ToastSession(title, Notifer)
            {
                Text1 = text,
                Buttons = new(buttons),
                Image = uri
            }.Show();
        }

        public IProgressBar CreateProgressNotification(string title)
        {
            return new ProgressBar(title, Notifer);
        }

        public void Dispose()
        {
            Register.UnRegistFromCurrentProcess();
            GC.SuppressFinalize(this);
        }
    }
}