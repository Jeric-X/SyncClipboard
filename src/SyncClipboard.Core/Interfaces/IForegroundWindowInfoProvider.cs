using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Interfaces;

public interface IForegroundWindowInfoProvider
{
    ForegroundWindowDetail? GetForegroundWindowDetail();
    ForegroundWindowDetail? GetWindowDetail(NativeWindowInfo window);
    ForegroundWindowInfo? GetForegroundWindowInfo();
    bool TryActivateWindow(NativeWindowInfo window);
}
