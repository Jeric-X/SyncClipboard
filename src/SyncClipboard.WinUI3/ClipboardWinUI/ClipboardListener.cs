using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using System;
using System.Threading;
using Windows.ApplicationModel.DataTransfer;

namespace SyncClipboard.WinUI3.ClipboardWinUI;

internal class ClipboardListener : ClipboardChangingListenerBase
{
    private MetaChanged? _action;
    protected override IClipboardFactory ClipboardFactory { get; }
    private readonly ILogger _logger;

    public ClipboardListener(IClipboardFactory clipboardFactory, ILogger logger)
    {
        ClipboardFactory = clipboardFactory;
        _logger = logger;
    }

    protected override void RegistSystemEvent(MetaChanged action)
    {
        _action = action;
        Clipboard.ContentChanged += HandleClipboardChanged;
    }

    protected override void UnRegistSystemEvent(MetaChanged action)
    {
        Clipboard.ContentChanged -= HandleClipboardChanged;
    }

    private void HandleClipboardChanged(object? _, object _1)
    {
        var timeout = TimeSpan.FromSeconds(10);
        using CancellationTokenSource cts = new(timeout);
        try
        {
            _action?.Invoke(null);
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
