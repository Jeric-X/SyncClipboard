using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva.ClipboardReader;

public interface IClipboardReader
{
    Task<string[]?> GetFormatsAsync(CancellationToken token);
    Task<string?> GetTextAsync(CancellationToken token);
    Task<object?> GetDataAsync(string format, CancellationToken token);
    [SupportedOSPlatform("linux")]
    Task<int?> GetTimeStamp(CancellationToken token);
}
