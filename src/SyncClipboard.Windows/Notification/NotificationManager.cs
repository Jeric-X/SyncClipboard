using Microsoft.Toolkit.Uwp.Notifications;
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
            var content = new ToastContentBuilder()
                .AddHeroImage(uri, "alternateText")
                .AddText(title)
                .AddText(text);
            foreach (var button in buttons)
            {
                content.AddButton(button);
            }
            content.Show();
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