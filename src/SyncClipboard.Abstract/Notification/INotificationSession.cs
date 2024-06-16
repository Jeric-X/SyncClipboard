namespace SyncClipboard.Abstract.Notification;

public interface INotificationSession
{
    public string Title { get; set; }
    public Uri? Image { get; set; }
    public List<Button> Buttons { get; set; }
}
