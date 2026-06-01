namespace SyncClipboard.Core.Models;

public readonly struct ForegroundWindowDetail
{
    public ForegroundWindowInfo? WindowInfo { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsValid { get; init; }

    public static readonly ForegroundWindowDetail Invalid = new()
    {
        WindowInfo = null,
        X = 0,
        Y = 0,
        Width = 0,
        Height = 0,
        IsValid = false
    };
}
