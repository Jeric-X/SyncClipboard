using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using System;
using System.Threading;
using Windows.ApplicationModel.DataTransfer;

namespace SyncClipboard.WinUI3.ClipboardWinUI;

internal class ClipboardListener : ClipboardChangingListenerBase
{
    private Action<ClipboardMetaInfomation>? _action;
    private readonly IClipboardFactory _clipboardFactory;
    protected override IClipboardFactory ClipboardFactory => _clipboardFactory;

    public ClipboardListener(IClipboardFactory clipboardFactory)
    {
        _clipboardFactory = clipboardFactory;
    }

    protected override void RegistSystemEvent(Action<ClipboardMetaInfomation> action)
    {
        _action = action;
        Clipboard.ContentChanged += HandleClipboardChanged;
    }

    protected override void UnRegistSystemEvent(Action<ClipboardMetaInfomation> action)
    {
        Clipboard.ContentChanged -= HandleClipboardChanged;
    }

    private async void HandleClipboardChanged(object? _, object _1)
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(3));
        _action?.Invoke(await _clipboardFactory.GetMetaInfomation(cts.Token));
    }
}
