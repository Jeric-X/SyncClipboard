using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Clipboard;

public abstract class ClipboardChangingListenerBase : IClipboardChangingListener, IDisposable
{
    private readonly object _countLocker = new object();
    private int _count = 0;

    protected abstract void RegistSystemEvent(Action<ClipboardMetaInfomation> action);
    protected abstract void UnRegistSystemEvent(Action<ClipboardMetaInfomation> action);
    protected abstract IClipboardFactory ClipboardFactory { get; }

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
        ChangedImpl?.Invoke(meta);
    };

    ~ClipboardChangingListenerBase() => Dispose();

#pragma warning disable CA1816 // Dispose 方法应调用 SuppressFinalize
    public void Dispose()
#pragma warning restore CA1816 // Dispose 方法应调用 SuppressFinalize
    {
        ChangedImpl = null;
        UnRegistSystemEvent(NotifyAll);
        _count = 0;
    }
}
