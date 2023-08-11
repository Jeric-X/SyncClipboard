using Microsoft.Toolkit.Uwp.Notifications;
using SyncClipboard.Core.Interfaces;
using Windows.UI.Notifications;

namespace SyncClipboard.Core.Utilities.Notification
{
    public class NotificationManager : IDisposable
    {
        public readonly ToastNotifier Notifer;
        private readonly ILogger _logger;

        public NotificationManager(ILogger logger)
        {
            Notifer = ToastNotificationManager.CreateToastNotifier(Register.RegistFromCurrentProcess());
            _logger = logger;
        }

        ~NotificationManager() => Dispose();

        public void SendText(string title, string text, params Button[] buttons)
        {
            new ToastSession(title, Notifer, _logger) { Text1 = text, Buttons = new(buttons) }.Show();
        }

#pragma warning disable CA1822 // 将成员标记为 static, 必须为non static, Notifer在内部被隐藏使用
        public void SendImage(string title, string text, Uri uri, params Button[] buttons)
#pragma warning restore CA1822 // 将成员标记为 static
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

        public ProgressBar CreateProgressNotification(string title)
        {
            return new ProgressBar(title, Notifer, _logger);
        }

        public void Dispose()
        {
            Register.UnRegistFromCurrentProcess();
            GC.SuppressFinalize(this);
        }
    }
}