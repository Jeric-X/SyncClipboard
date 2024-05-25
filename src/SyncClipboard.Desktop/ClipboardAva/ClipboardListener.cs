using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva;

internal class ClipboardListener : ClipboardChangingListenerBase
{
    private readonly IClipboardFactory _clipboardFactory;
    private readonly ILogger _logger;

    private Timer? _timer;
    private ClipboardChangedDelegate? _action;
    private ClipboardMetaInfomation? _meta;

    private readonly SemaphoreSlim _tickSemaphore = new(1, 1);
    private CancellationTokenSource? _cts;

    public ClipboardListener(IClipboardFactory clipboardFactory, ILogger logger)
    {
        _clipboardFactory = clipboardFactory;
        _logger = logger;
    }

    protected override void RegistSystemEvent(ClipboardChangedDelegate action)
    {
        _action = action;
        _timer = new Timer(InvokeTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    protected override void UnRegistSystemEvent(ClipboardChangedDelegate action)
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
            _cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var meta = await _clipboardFactory.GetMetaInfomation(_cts.Token);
            if (meta == _meta)
            {
                return;
            }

            if (_meta is not null)
            {
                _meta = meta;
                var profile = await _clipboardFactory.CreateProfileFromMeta(meta, _cts.Token);
                _ = Task.Run(() => _action?.Invoke(meta, profile));
                _ = _logger.WriteAsync($"Clipboard changed to {meta}");
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
