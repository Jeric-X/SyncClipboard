namespace SyncClipboard.Abstract;

public class Callbacker
{
    public string Argument { get; set; }
    public Action<string> CallBack { get; set; }
    public bool Pedding { get; set; } = false;
    public Callbacker(string argument, Action<string> callback, bool pedding = false)
    {
        Argument = argument;
        CallBack = callback;
        Pedding = pedding;
    }
}

public class Button
{
    public string Text { get; set; } = "Button";
    public Callbacker? Callbacker { get; set; }
    public Button(string text, Callbacker? callbacker)
    {
        Text = text;
        Callbacker = callbacker;
    }

    public Button(string text, Action action)
    {
        Text = text;
        Callbacker = new(Guid.NewGuid().ToString(), (_) => action());
    }
}