using SyncClipboard.Abstract.Notification;

namespace SyncClipboard.Core.Utilities.Fake;

public class FakeNotification : INotification
{
    public IProgressBar CreateProgressNotification(string title)
    {
        return new FakeProgressBar();
    }

    public void SendImage(string title, string text, Uri uri, params Button[] buttons)
    {
    }

    public void SendTemporary(NotificationPara para)
    {
    }

    public void SendText(string title, string text, params Button[] buttons)
    {
    }
}
