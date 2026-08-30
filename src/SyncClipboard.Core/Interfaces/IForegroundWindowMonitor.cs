using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Interfaces;

public interface IForegroundWindowMonitor
{
    event Action<WindowDetail?>? ForegroundWindowChanged;

    WindowDetail? GetCurrentForegroundWindow();
}
