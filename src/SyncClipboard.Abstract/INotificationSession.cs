namespace SyncClipboard.Abstract;

public interface INotificationSession
{
    public Uri? Image { get; set; }
    public List<Button> Buttons { get; set; }
}
