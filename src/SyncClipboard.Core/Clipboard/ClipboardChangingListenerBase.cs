using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.Clipboard;

public abstract class ClipboardChangingListenerBase : IClipboardChangingListener, IClipboardMoniter, IDisposable
{
    private static readonly TimeSpan ClipboardReadTimeout = TimeSpan.FromSeconds(10);

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
            ChangedImpl += value;
            AddRef();
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
            ClipboardChangedImpl += value;
            AddRef();
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

    private async void NotifyAll(ClipboardMetaInfomation? meta)
    {
        using var cancellationSource = new CancellationTokenSource(ClipboardReadTimeout);
        var token = cancellationSource.Token;

        try
        {
            ClipboardChangedImpl?.GetInvocationList()?.ForEach(delegt => delegt.InvokeNoExcept());
            meta ??= await ClipboardFactory.GetMetaInfomation(token);
            var profile = await ClipboardFactory.CreateProfileFromMeta(meta, token);
            ChangedImpl?.GetInvocationList()?.ForEach(delegt => delegt.InvokeNoExcept(meta, profile));
        }
        catch (Exception ex)
        {
            AppCore.Current?.Logger.Write($"Clipboard handler unhandled exception {ex.Message}\n{ex.StackTrace}");
        }
    }

    ~ClipboardChangingListenerBase() => Dispose();

    public void Dispose()
    {
        ChangedImpl = null;
        UnRegistSystemEvent(NotifyAll);
        GC.SuppressFinalize(this);
    }
}
