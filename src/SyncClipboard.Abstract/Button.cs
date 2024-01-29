namespace SyncClipboard.Abstract;

public class Button
{
    public string Text { get; set; } = "Button";
    public string Uid { get; } = Guid.NewGuid().ToString();
    public Action Callbacker { get; set; }

    public Button(string text, Action action)
    {
        Text = text;
        Callbacker = action;
    }

    public void Invoke()
    {
        Callbacker.Invoke();
    }
}