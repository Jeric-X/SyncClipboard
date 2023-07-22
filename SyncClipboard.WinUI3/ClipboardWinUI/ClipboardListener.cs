using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using Windows.ApplicationModel.DataTransfer;

namespace SyncClipboard.WinUI3.ClipboardWinUI;

internal class ClipboardListener : IClipboardChangingListener, IDisposable
{
    public event Action<ClipboardMetaInfomation>? Changed;

    private readonly Core.Clipboard.IClipboardFactory _clipboardFactory;

    public ClipboardListener(Core.Clipboard.IClipboardFactory clipboardFactory)
    {
        _clipboardFactory = clipboardFactory;
        Clipboard.ContentChanged += HandleClipboardChanged;
    }

    ~ClipboardListener() => Dispose();

    private void HandleClipboardChanged(object? _, object obj)
    {
        var meta = _clipboardFactory.GetMetaInfomation();
        Changed?.Invoke(meta);
    }

    private bool _disposed = false;

    public void Dispose()
    {
        if (!_disposed)
        {
            Clipboard.ContentChanged -= HandleClipboardChanged;
            GC.SuppressFinalize(this);
            _disposed = true;
        }
    }
}
