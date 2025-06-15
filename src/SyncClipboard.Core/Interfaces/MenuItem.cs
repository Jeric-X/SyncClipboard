namespace SyncClipboard.Core.Interfaces;

public class MenuItem(string? text, Action? action = null)
{
    public string? Text { get; set; } = text;
    public Action? Action { get; set; } = action;
}