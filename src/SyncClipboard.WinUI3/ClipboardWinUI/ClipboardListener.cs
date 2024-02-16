using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Threading;
using Windows.ApplicationModel.DataTransfer;

namespace SyncClipboard.WinUI3.ClipboardWinUI;

internal class ClipboardListener : ClipboardChangingListenerBase
{
    private Action<ClipboardMetaInfomation>? _action;
    private readonly IClipboardFactory _clipboardFactory;
    private readonly ILogger _logger;

    public ClipboardListener(IClipboardFactory clipboardFactory, ILogger logger)
    {
        _clipboardFactory = clipboardFactory;
        _logger = logger;
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
        var timeout = TimeSpan.FromSeconds(3);
        using CancellationTokenSource cts = new(timeout);
        try
        {
            _action?.Invoke(await _clipboardFactory.GetMetaInfomation(cts.Token));
        }
        catch (Exception ex)
        {
            if (cts.IsCancellationRequested)
            {
                _logger.Write($"Get clipboard timeout after {timeout.TotalSeconds} seconds");
            }
            else
            {
                _logger.Write(ex.Message);
            }
        }
    }
}
