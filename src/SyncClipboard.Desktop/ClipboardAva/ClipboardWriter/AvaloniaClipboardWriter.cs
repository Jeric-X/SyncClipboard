using Avalonia.Input.Platform;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva.ClipboardWriter;

internal sealed class AvaloniaClipboardWriter(IClipboard clipboard) : IClipboardWriter
{
    public string SourceName => "Avalonia";
    public bool SupportsMultipleFormats => true;

    public Task SetTextAsync(string text, CancellationToken token) =>
        clipboard.SetTextAsync(text).WaitAsync(token);

    public Task SetDataAsync(AutoDisposeDataTransfer transfer, CancellationToken token) =>
        clipboard.SetDataAsync(transfer).WaitAsync(token);
}
