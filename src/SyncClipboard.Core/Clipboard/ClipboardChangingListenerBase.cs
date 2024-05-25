using SyncClipboard.Abstract;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Clipboard;

public abstract class ClipboardChangingListenerBase : IClipboardChangingListener, IClipboardMoniter, IDisposable
{
    private readonly object _countLocker = new object();
    private int _count = 0;

    protected delegate void MetaChanged(ClipboardMetaInfomation? meta);
    protected abstract void RegistSystemEvent(MetaChanged action);
    protected abstract void UnRegistSystemEvent(MetaChanged action);
    protected abstract IClipboardFactory ClipboardFactory { get; }

    private event ClipboardChangedDelegate? ChangedImpl;
    public event ClipboardChangedDelegate? Changed
    {
        add
        {
            AddRef();
            ChangedImpl += value;
        }
        remove
        {
            ChangedImpl -= value;
            ReleaseRef();
        }
    }

    private event Action? ClipboardChangedImpl;
    public event Action? ClipboardChanged
    {
        add
        {
            AddRef();
            ClipboardChangedImpl += value;
        }
        remove
        {
            ClipboardChangedImpl -= value;
            ReleaseRef();
        }
    }

    private void AddRef()
    {
        lock (_countLocker)
        {
            if (_count == 0)
            {
                _count++;
                RegistSystemEvent(NotifyAll);
            }
        }
    }

    private void ReleaseRef()
    {
        lock (_countLocker)
        {
            _count--;
            if (_count == 0)
            {
                UnRegistSystemEvent(NotifyAll);
            }
        }
    }

    private MetaChanged NotifyAll => async (meta) =>
    {
        try
        {
            ClipboardChangedImpl?.Invoke();
            meta ??= await ClipboardFactory.GetMetaInfomation(new CancellationTokenSource(1000).Token);
            var profile = await ClipboardFactory.CreateProfileFromMeta(meta, new CancellationTokenSource(1000).Token);
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
