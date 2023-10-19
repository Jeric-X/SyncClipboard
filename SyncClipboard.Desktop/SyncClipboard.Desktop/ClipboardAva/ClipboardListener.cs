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

    private void InvokeTick(object? _)
    {
        var meta = _clipboardFactory.GetMetaInfomation();
        if (meta != _meta)
        {
            _action?.Invoke(meta);
            _meta = meta;
        }
    }
}
