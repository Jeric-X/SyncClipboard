using Avalonia.Input;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva.ClipboardWriter;

public interface IClipboardWriter
{
    string SourceName { get; }
    Task SetTextAsync(string text, CancellationToken token);
    Task SetDataAsync(DataTransfer transfer, CancellationToken token);
}
