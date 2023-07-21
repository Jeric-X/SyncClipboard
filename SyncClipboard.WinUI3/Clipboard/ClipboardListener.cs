using Microsoft.UI.Xaml;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Runtime.InteropServices;
using WinUIEx;
using WinUIEx.Messaging;

namespace SyncClipboard.WinUI3.Clipboard;

internal class ClipboardListener : IClipboardChangingListener, IDisposable
{
    public event Action<ClipboardMetaInfomation>? Changed;

    private const int WM_CLIPBOARDUPDATE = 0x031D;

    private readonly WindowMessageMonitor _windowMessageMonitor;
    private readonly IClipboardFactory _clipboardFactory;
    private readonly IntPtr _windowHandle;

    [DllImport("user32.dll")]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    public ClipboardListener(Window window, IClipboardFactory clipboardFactory)
    {
        _clipboardFactory = clipboardFactory;
        _windowHandle = window.GetWindowHandle();
        AddClipboardFormatListener(_windowHandle);
        _windowMessageMonitor = new WindowMessageMonitor(_windowHandle);
        _windowMessageMonitor.WindowMessageReceived += HandleWindowsMessage;
    }

    ~ClipboardListener() => Dispose();

    private void HandleWindowsMessage(object? _, WindowMessageEventArgs eventArgs)
    {
        if (eventArgs.Message.MessageId == WM_CLIPBOARDUPDATE)
        {
            Changed?.Invoke(_clipboardFactory.GetMetaInfomation());
        }
    }

    private bool _disposed = false;

    public void Dispose()
    {
        if (!_disposed)
        {
            RemoveClipboardFormatListener(_windowHandle);
            _windowMessageMonitor.Dispose();
            GC.SuppressFinalize(this);
            _disposed = true;
        }
    }
}
