using SyncClipboard.Abstract;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.Clipboard;

public abstract class ClipboardChangingListenerBase : IClipboardChangingListener, IClipboardMoniter, IDisposable
{
    private bool _registed = false;

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
        lock (this)
        {
            if (_registed)
                return;
            if (HasClipboardHandler())
            {
                RegistSystemEvent(NotifyAll);
                _registed = true;
            }
        }
    }

    private void ReleaseRef()
    {
        lock (this)
        {
            if (!_registed)
                return;
            if (HasClipboardHandler() is false)
            {
                UnRegistSystemEvent(NotifyAll);
                _registed = false;
            }
        }
    }

    private bool HasClipboardHandler()
    {
        return ClipboardChangedImpl?.GetInvocationList().Length > 0 || ChangedImpl?.GetInvocationList().Length > 0;
    }

    private MetaChanged NotifyAll => async (meta) =>
    {
        try
        {
            ClipboardChangedImpl?.GetInvocationList()?.ForEach(delegt => delegt.InvokeNoExcept());
            var token = new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
            meta ??= await ClipboardFactory.GetMetaInfomation(token);
            var profile = await ClipboardFactory.CreateProfileFromMeta(meta, token);
            ChangedImpl?.GetInvocationList()?.ForEach(delegt => delegt.InvokeNoExcept(meta, profile));
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
        GC.SuppressFinalize(this);
    }
}
