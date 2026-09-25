using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva.ClipboardReader;

public interface IClipboardReader
{
    public string SourceName { get; }
    Task<string[]?> GetFormatsAsync(CancellationToken token);
    Task<string?> GetTextAsync(CancellationToken token);
    Task<object?> GetDataAsync(string format, CancellationToken token);

    /// <summary>Reads a text format, decoding byte data as UTF-8 and preserving string data.</summary>
    async Task<string?> GetStringAsync(string format, CancellationToken token)
    {
        return await GetDataAsync(format, token).ConfigureAwait(false) switch
        {
            string text => text,
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            _ => null
        };
    }

    Task<Bitmap?> GetBitmapAsync(CancellationToken token);
    Task<IStorageItem[]?> GetFilesAsync(CancellationToken token);
    [SupportedOSPlatform("linux")]
    Task<int?> GetTimeStamp(CancellationToken token);
}
