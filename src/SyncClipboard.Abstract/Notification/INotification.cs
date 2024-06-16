namespace SyncClipboard.Abstract.Notification;

public interface INotification
{
    public void SendText(string title, string text, params Button[] buttons);
    public void SendImage(string title, string text, Uri uri, params Button[] buttons);
    public void SendTemporary(NotificationPara para);
    public IProgressBar CreateProgressNotification(string title);
}
