namespace SyncClipboard.Core.Models;

public abstract record NativeWindowInfo
{
    public required int ProcessId { get; init; }

    public abstract bool IsSameWindow(NativeWindowInfo other);
}

public sealed record WindowsNativeWindowInfo : NativeWindowInfo
{
    public required nint WindowHandle { get; init; }

    public override bool IsSameWindow(NativeWindowInfo other) =>
        other is WindowsNativeWindowInfo windows
        && ProcessId == windows.ProcessId
        && WindowHandle == windows.WindowHandle;
}

public sealed record X11NativeWindowInfo : NativeWindowInfo
{
    public required string DisplayName { get; init; }
    public required nuint WindowId { get; init; }

    public override bool IsSameWindow(NativeWindowInfo other) =>
        other is X11NativeWindowInfo x11
        && string.Equals(DisplayName, x11.DisplayName, StringComparison.Ordinal)
        && WindowId == x11.WindowId;
}

public sealed record MacNativeWindowInfo : NativeWindowInfo
{
    public required string BundleIdentifier { get; init; }
    public long? WindowNumber { get; init; }
    public string WindowTitle { get; init; } = string.Empty;
    public ScreenPosition? Bounds { get; init; }

    public override bool IsSameWindow(NativeWindowInfo other)
    {
        if (other is not MacNativeWindowInfo mac || ProcessId != mac.ProcessId)
        {
            return false;
        }

        if (WindowNumber.HasValue && mac.WindowNumber.HasValue)
        {
            return WindowNumber.Value == mac.WindowNumber.Value;
        }

        if (!WindowNumber.HasValue
            && !mac.WindowNumber.HasValue
            && string.IsNullOrEmpty(WindowTitle)
            && string.IsNullOrEmpty(mac.WindowTitle))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(WindowTitle) && !string.IsNullOrEmpty(mac.WindowTitle))
        {
            return string.Equals(WindowTitle, mac.WindowTitle, StringComparison.Ordinal);
        }

        return false;
    }
}
