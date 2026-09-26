using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using System.Runtime.Versioning;
using System.Text;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace SyncClipboard.Desktop.ClipboardAva.ClipboardReader;

public class AvaloniaClipboardReader(IClipboard clipboard) : IClipboardReader
{
    private readonly IClipboard _clipboard = clipboard;

    public string SourceName => "Avalonia";

    private async Task<T> ReadAsync<T>(Func<IAsyncDataTransfer?, Task<T>> read, CancellationToken token)
    {
        return await Dispatcher.UIThread.InvokeAsync<T>(async () =>
        {
            var dataTransfer = await _clipboard.TryGetDataAsync().WaitAsync(token);
            // X11 读取自身剪贴板会返回仍由平台持有的原包，此时仅借用，不能在读取后释放。
            using var ownedTransfer = dataTransfer is AutoDisposeDataTransfer ? null : dataTransfer;
            return await read(dataTransfer);
        }, DispatcherPriority.Normal);
    }

    public Task<string[]?> GetFormatsAsync(CancellationToken token) =>
        ReadAsync(data => Task.FromResult(data?.Formats.Select(f => f.Identifier).ToArray()), token);

    public Task<string?> GetTextAsync(CancellationToken token) =>
        ReadAsync(data => data is null ? Task.FromResult<string?>(null) : data.TryGetTextAsync().WaitAsync(token), token);

    public Task<Bitmap?> GetBitmapAsync(CancellationToken token)
    {
        return ReadAsync(async data =>
        {
            if (data is null) return null;
            var bitmap = await data.TryGetBitmapAsync().WaitAsync(token);
            return CopyBorrowedBitmap(data, bitmap);
        }, token);
    }

    // 调用者会释放读取结果，因此不能把借用包中的原 Bitmap 直接交出去。
    private static Bitmap? CopyBorrowedBitmap(IAsyncDataTransfer data, Bitmap? bitmap) =>
        data is AutoDisposeDataTransfer && bitmap is not null ? bitmap.CreateScaledBitmap(bitmap.PixelSize) : bitmap;

    public Task<IStorageItem[]?> GetFilesAsync(CancellationToken token) =>
        ReadAsync(data => data is null ? Task.FromResult<IStorageItem[]?>(null) : data.TryGetFilesAsync().WaitAsync(token), token);

    public async Task<object?> GetDataAsync(string format, CancellationToken token)
    {
        if (OperatingSystem.IsLinux())
        {
            if (format == Format.Targets)
            {
                return await GetFormatsAsync(token);
            }
            else if (format == Format.TimeStamp)
            {
                return await GetTimeStamp(token);
            }
        }

        return await ReadAsync(async dataTransfer =>
        {
            if (dataTransfer is null)
                return null;

            var dataFormat = dataTransfer.Formats.FirstOrDefault(candidate => candidate.Identifier == format);
            if (dataFormat is null)
                return null;

            var item = dataTransfer.GetItems(dataFormat).FirstOrDefault();
            var value = item is null ? null : await item.TryGetRawAsync(dataFormat).WaitAsync(token);
            return value is Bitmap bitmap ? CopyBorrowedBitmap(dataTransfer, bitmap) : value;
        }, token);
    }

    [SupportedOSPlatform("linux")]
    public Task<int?> GetTimeStamp(CancellationToken token)
    {
        return ReadAsync<int?>(async dataTransfer =>
        {
            if (dataTransfer is null)
                return null;

            var platformFormat = DataFormat.CreateBytesPlatformFormat(Format.TimeStamp);
            var bytes = await dataTransfer.TryGetValueAsync(platformFormat).WaitAsync(token);
            if (bytes is null)
                return null;

            var str = Encoding.UTF8.GetString(bytes);
            bool canParse = int.TryParse(str, out var result);
            return canParse ? result : BitConverter.ToInt32(bytes);
        }, token);
    }
}
