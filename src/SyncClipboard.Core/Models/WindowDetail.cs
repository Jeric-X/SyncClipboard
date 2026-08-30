namespace SyncClipboard.Core.Models;

public readonly struct WindowDetail
{
    public WindowInfo? WindowInfo { get; init; }
    public ScreenPosition? Bounds { get; init; }
    public NativeWindowInfo? NativeWindowInfo { get; init; }
}
