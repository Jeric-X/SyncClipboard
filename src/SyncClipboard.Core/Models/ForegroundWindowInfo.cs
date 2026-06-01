namespace SyncClipboard.Core.Models;

public readonly struct ForegroundWindowInfo
{
    public string ProcessName { get; init; }
    public string WindowTitle { get; init; }
    public string ExecutableName { get; init; }
}
