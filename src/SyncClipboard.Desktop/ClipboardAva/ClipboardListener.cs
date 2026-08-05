using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva;

internal class ClipboardListener(
    IClipboardFactory clipboardFactory,
    IClipboardFingerprintProvider fingerprintProvider,
    ILogger logger) : ClipboardChangingListenerBase
{
    protected override IClipboardFactory ClipboardFactory { get; } = clipboardFactory;
    private readonly IClipboardFingerprintProvider _fingerprintProvider = fingerprintProvider;
    private readonly ILogger _logger = logger;

    private Timer? _timer;
    private MetaChanged? _action;
    private ClipboardMetaInfomation? _meta;

    private int? _lastFingerprint;

    private readonly SemaphoreSlim _tickSemaphore = new(1, 1);
    private CancellationTokenSource? _cts;

    protected override void RegistSystemEvent(MetaChanged action)
    {
        _action = action;
        _timer = new Timer(InvokeTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    protected override void UnRegistSystemEvent(MetaChanged action)
    {
        _timer?.Dispose();
        _timer = null;

        _action = null;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    internal void TriggerClipboardChangedEvent()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        InvokeTick(null);
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

            var currentFingerprint = await _fingerprintProvider.GetClipboardFingerprint(_cts.Token);

            if (currentFingerprint is not null && currentFingerprint == _lastFingerprint)
            {
                return;
            }

            var meta = await ((ClipboardFactory)ClipboardFactory).GetMetaInfomation(currentFingerprint, _cts.Token);
            if (meta == _meta)
            {
                return;
            }

            _lastFingerprint = currentFingerprint;
            _meta = meta;
            if (_meta is not null)
            {
                ClipboardFactory.SetClipboardOwner(_meta);
                _ = Task.Run(() => _action?.Invoke(meta));
                _ = _logger.WriteAsync($"Clipboard changed to {meta}");
            }
        }
        catch { }
        finally
        {
            _tickSemaphore.Release();
        }
    }
}
