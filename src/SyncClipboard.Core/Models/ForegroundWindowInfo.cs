namespace SyncClipboard.Core.Models;

public readonly struct ForegroundWindowInfo
{
    public string ProcessName { get; init; }
    public string WindowTitle { get; init; }
    public string ExecutableName { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsValid { get; init; }

    public static readonly ForegroundWindowInfo Invalid = new()
    {
        ProcessName = "",
        WindowTitle = "",
        ExecutableName = "",
        X = 0,
        Y = 0,
        Width = 0,
        Height = 0,
        IsValid = false
    };
}
