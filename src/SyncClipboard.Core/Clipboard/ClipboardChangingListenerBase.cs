using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Clipboard;

public abstract class ClipboardChangingListenerBase : IClipboardChangingListener, IDisposable
{
    private readonly object _countLocker = new object();
    private int _count = 0;

    protected abstract void RegistSystemEvent(Action<ClipboardMetaInfomation> action);
    protected abstract void UnRegistSystemEvent(Action<ClipboardMetaInfomation> action);

    private event Action<ClipboardMetaInfomation>? ChangedImpl;
    public event Action<ClipboardMetaInfomation>? Changed
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

    private Action<ClipboardMetaInfomation> NotifyAll => (meta) =>
    {
        try
        {
            ChangedImpl?.Invoke(meta);
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
