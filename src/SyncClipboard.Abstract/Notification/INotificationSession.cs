namespace SyncClipboard.Abstract.Notification;

public interface INotificationSession
{
    public Uri? Image { get; set; }
    public List<Button> Buttons { get; set; }
}
