namespace SyncClipboard.Abstract.Notification;

public class Button(string text, Action action)
{
    public string Text { get; set; } = text;
    public string Uid { get; } = Guid.NewGuid().ToString();
    public Action Callbacker { get; set; } = action;

    public void Invoke()
    {
        Callbacker.Invoke();
    }
}