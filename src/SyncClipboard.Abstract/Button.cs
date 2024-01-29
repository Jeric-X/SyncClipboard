namespace SyncClipboard.Abstract;

public class Button
{
    public string Text { get; set; } = "Button";
    public readonly string Uid;
    public Action Callbacker { get; set; }

    public Button(string text, Action action)
    {
        Text = text;
        Uid = Guid.NewGuid().ToString();
        Callbacker = action;
    }

    public void Invoke()
    {
        Callbacker.Invoke();
    }
}