using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Interfaces;

public interface IForegroundWindowMonitor
{
    event Action<ForegroundWindowDetail?>? ForegroundWindowChanged;

    ForegroundWindowDetail? GetCurrentForegroundWindow();
}
