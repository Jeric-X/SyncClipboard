using SyncClipboard.Core.Interfaces;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva.ClipboardWriter;

public interface IClipboardWriter : IClipboardWriteCapabilities
{
    Task SetTextAsync(string text, CancellationToken token);
    // 调用即转移数据包的所有权，调用方此后不再负责释放。
    Task SetDataAsync(AutoDisposeDataTransfer transfer, CancellationToken token);
}
