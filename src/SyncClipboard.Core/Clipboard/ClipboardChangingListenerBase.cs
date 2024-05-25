using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Core.Clipboard;

public abstract class ClipboardChangingListenerBase : IClipboardChangingListener, IDisposable
{
    private readonly object _countLocker = new object();
    private int _count = 0;

    protected abstract void RegistSystemEvent(ClipboardChangedDelegate action);
    protected abstract void UnRegistSystemEvent(ClipboardChangedDelegate action);

    private event ClipboardChangedDelegate? ChangedImpl;
    public event ClipboardChangedDelegate? Changed
    {
        add
        {
            lock (_countLocker)
            {
                if (_count == 0)
                {
                    _count++;
                    RegistSystemEvent(NotifyAll);
                }
            }
            ChangedImpl += value;
        }
        remove
        {
            ChangedImpl -= value;
            lock (_countLocker)
            {
                _count--;
                if (_count == 0)
                {
                    UnRegistSystemEvent(NotifyAll);
                }
            }
        }
    }

    private ClipboardChangedDelegate NotifyAll => (meta, profile) =>
    {
        try
        {
            ChangedImpl?.Invoke(meta, profile);
        }
        catch (Exception ex)
        {
            AppCore.Current?.Logger.Write($"Clipboard handler unhandled exception {ex.Message}\n{ex.StackTrace}");
        }
    };

    ~ClipboardChangingListenerBase() => Dispose();

    public void Dispose()
    {
        ChangedImpl = null;
        UnRegistSystemEvent(NotifyAll);
        _count = 0;
        GC.SuppressFinalize(this);
    }
}
