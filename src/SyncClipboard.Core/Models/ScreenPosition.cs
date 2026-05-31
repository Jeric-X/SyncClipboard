namespace SyncClipboard.Core.Models;

public readonly struct ScreenPosition
{
    public int X { get; init; }
    public int Y { get; init; }
    public bool IsValid { get; init; }

    public static readonly ScreenPosition Invalid = new() { X = 0, Y = 0, IsValid = false };
}
