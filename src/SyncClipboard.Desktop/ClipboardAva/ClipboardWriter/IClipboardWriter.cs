using Avalonia.Input;
using SyncClipboard.Core.Interfaces;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva.ClipboardWriter;

public interface IClipboardWriter : IClipboardWriteCapabilities
{
    Task SetTextAsync(string text, CancellationToken token);
    Task SetDataAsync(DataTransfer transfer, CancellationToken token);
}
