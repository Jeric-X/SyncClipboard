using Avalonia.Input;
using Avalonia.Input.Platform;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva.ClipboardWriter;

internal sealed class AvaloniaClipboardWriter(IClipboard clipboard) : IClipboardWriter
{
    private WeakReference<AutoDisposeDataTransfer>? _lastTransfer;

    public string SourceName => "Avalonia";
    public bool SupportsMultipleFormats => true;

    public Task SetTextAsync(string text, CancellationToken token)
    {
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(text));
        return SetDataAsync(new AutoDisposeDataTransfer(data), token);
    }

    public async Task SetDataAsync(AutoDisposeDataTransfer transfer, CancellationToken token)
    {
        AutoDisposeDataTransfer? previous = null;
        _lastTransfer?.TryGetTarget(out previous);

        // 等待取消或写入失败时，保留原记录，本次包交由平台或终结器清理。
        await clipboard.SetDataAsync(transfer).WaitAsync(token);

        _lastTransfer = new(transfer);
        if (!ReferenceEquals(previous, transfer)) previous?.Dispose();
    }
}
