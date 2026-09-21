using Avalonia.Input;
using Avalonia.Input.Platform;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva.ClipboardWriter;

internal sealed class AvaloniaClipboardWriter : IClipboardWriter
{
    public string SourceName => "Avalonia";

    public Task SetTextAsync(string text, CancellationToken token) =>
        App.Current.Clipboard.SetTextAsync(text).WaitAsync(token);

    public async Task SetDataAsync(DataTransfer transfer, CancellationToken token)
    {
        // Preserve the existing Avalonia data-write error handling.
        try
        {
            await App.Current.Clipboard.SetDataAsync(transfer).WaitAsync(token);
        }
        catch { }
    }
}
