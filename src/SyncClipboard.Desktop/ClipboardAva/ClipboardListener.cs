using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System;
using System.Threading;

namespace SyncClipboard.Desktop.ClipboardAva;

internal class ClipboardListener : ClipboardChangingListenerBase
{
    private readonly IClipboardFactory _clipboardFactory;
    protected override IClipboardFactory ClipboardFactory => _clipboardFactory;

    private Timer? _timer;
    private Action<ClipboardMetaInfomation>? _action;
    private ClipboardMetaInfomation? _meta;

    private readonly SemaphoreSlim _tickSemaphore = new(1, 1);
    private CancellationTokenSource? _cts;

    public ClipboardListener(IClipboardFactory clipboardFactory)
    {
        _clipboardFactory = clipboardFactory;
    }

    protected override void RegistSystemEvent(Action<ClipboardMetaInfomation> action)
    {
        _action = action;
        _timer = new Timer(InvokeTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    protected override void UnRegistSystemEvent(Action<ClipboardMetaInfomation> action)
    {
        _timer?.Dispose();
        _timer = null;

        _action = null;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async void InvokeTick(object? _)
    {
        if (_tickSemaphore.Wait(0) is false)
        {
            return;
        }

        try
        {
            _cts?.Dispose();
            _cts = new CancellationTokenSource(TimeSpan.FromSeconds(1000));

            var meta = await _clipboardFactory.GetMetaInfomation(_cts.Token);
            if (meta == _meta)
            {
                return;
            }

            if (_meta is not null)
            {
                _meta = meta;
                _action?.Invoke(meta);
            }
            else
            {
                _meta = meta;
            }
        }
        catch { }
        finally
        {
            _tickSemaphore.Release();
        }
    }
}
