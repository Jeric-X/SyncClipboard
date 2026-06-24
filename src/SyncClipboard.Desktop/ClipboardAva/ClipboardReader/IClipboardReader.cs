using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva.ClipboardReader;

public interface IClipboardReader
{
    public string SourceName { get; }
    Task<string[]?> GetFormatsAsync(CancellationToken token);
    Task<string?> GetTextAsync(CancellationToken token);
    Task<object?> GetDataAsync(string format, CancellationToken token);
    Task<Bitmap?> GetBitmapAsync(CancellationToken token);
    Task<IStorageItem[]?> GetFilesAsync(CancellationToken token);
    [SupportedOSPlatform("linux")]
    Task<int?> GetTimeStamp(CancellationToken token);
}
