using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Runner;

namespace SyncClipboard.Core.Clipboard;

public abstract class ClipboardChangingListenerBase : IClipboardChangingListener, IClipboardMoniter, IDisposable
{
    private static readonly TimeSpan ClipboardChangeDebounceDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan ClipboardReadTimeout = TimeSpan.FromSeconds(10);

    private bool _registed = false;
    private readonly SingletonTask _notifyTask = new();

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

    private void NotifyAll(ClipboardMetaInfomation? meta)
    {
        _ = _notifyTask.Run(token => NotifyAllAsync(meta, token));
    }

    private async Task NotifyAllAsync(ClipboardMetaInfomation? meta, CancellationToken pendingToken)
    {
        try
        {
            // WinUI may raise ContentChanged synchronously from Clipboard.Flush().
            // Always leave the native callback before reading the clipboard to avoid
            // re-entering WinRT clipboard APIs on the same UI stack. The short delay
            // also coalesces bursts such as consecutive screenshots.
            await Task.Delay(ClipboardChangeDebounceDelay, pendingToken);

            ClipboardChangedImpl?.GetInvocationList()?.ForEach(delegt => delegt.InvokeNoExcept());
            using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(pendingToken);
            cancellationSource.CancelAfter(ClipboardReadTimeout);
            var token = cancellationSource.Token;
            meta ??= await ClipboardFactory.GetMetaInfomation(token);
            var profile = await ClipboardFactory.CreateProfileFromMeta(meta, token);
            ChangedImpl?.GetInvocationList()?.ForEach(delegt => delegt.InvokeNoExcept(meta, profile));
        }
        catch (OperationCanceledException) when (pendingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppCore.Current?.Logger.Write($"Clipboard handler unhandled exception {ex.Message}\n{ex.StackTrace}");
        }
    }

    ~ClipboardChangingListenerBase() => Dispose();

    public void Dispose()
    {
        _notifyTask.Cancel();
        ChangedImpl = null;
        UnRegistSystemEvent(NotifyAll);
        GC.SuppressFinalize(this);
    }
}
