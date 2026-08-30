namespace SyncClipboard.Core.Models;

public readonly struct ForegroundWindowDetail
{
    public ForegroundWindowInfo? WindowInfo { get; init; }
    public ScreenPosition? Bounds { get; init; }
    public NativeWindowInfo? NativeWindowInfo { get; init; }
}
