using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Interfaces;

public interface IForegroundWindowWatcher : IDisposable
{
    event Action<NativeWindowInfo?>? ForegroundWindowChanged;

    void Start();
    void Stop();
}
