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

    private readonly object _lock = new object();
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
        _action = null;
    }

    private async void InvokeTick(object? _)
    {
        lock (_lock)
        {
            if (_cts is not null && _cts.IsCancellationRequested is false)
            {
                return;
            }
            _cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        }

        try
        {
            var meta = await _clipboardFactory.GetMetaInfomation(_cts.Token);
            if (meta != _meta)
            {
                _action?.Invoke(meta);
                _meta = meta;
            }
        }
        catch { }
        finally
        {
            lock (_lock)
            {
                _cts.Dispose();
                _cts = null;
            }
        }
    }
}
