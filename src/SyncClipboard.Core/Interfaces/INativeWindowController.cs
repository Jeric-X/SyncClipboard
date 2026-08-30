using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Interfaces;

public interface INativeWindowController
{
    WindowDetail? GetForegroundWindowDetail();
    WindowDetail? GetWindowDetail(NativeWindowInfo window);
    WindowInfo? GetForegroundWindowInfo();
    bool TryActivateWindow(NativeWindowInfo window);
}
