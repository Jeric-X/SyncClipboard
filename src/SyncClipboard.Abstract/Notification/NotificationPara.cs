namespace SyncClipboard.Abstract.Notification;

public class NotificationPara
{
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public Uri? Image { get; set; } = null;
    public TimeSpan? Duration { get; set; } = null;
    public IEnumerable<Button> Buttons { get; set; } = [];

    public NotificationPara(string title, string text = "")
    {
        Title = title;
        Text = text;
    }

    public NotificationPara()
    {
    }
}