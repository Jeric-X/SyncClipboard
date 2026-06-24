using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Avalonia.Controls;
using SyncClipboard.Core.Interfaces;
using System.Runtime.Versioning;
using System.Text;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace SyncClipboard.Desktop.ClipboardAva.ClipboardReader;

public class AvaloniaClipboardReader(IMainWindow mainWindow) : IClipboardReader
{
    private readonly IClipboard _clipboard = (mainWindow as Window)?.Clipboard ?? throw new ArgumentNullException(nameof(mainWindow));

    public string SourceName => "Avalonia";

    public async Task<string[]?> GetFormatsAsync(CancellationToken token)
    {
        return await Dispatcher.UIThread.InvokeAsync<string[]?>(async () =>
        {
            var formats = await _clipboard.GetDataFormatsAsync().WaitAsync(token);
            return formats?.Select(f => f.Identifier).ToArray();
        }, DispatcherPriority.Normal);
    }

    public async Task<string?> GetTextAsync(CancellationToken token)
    {
        return await Dispatcher.UIThread.InvokeAsync<string?>(async () =>
        {
            return await _clipboard.TryGetTextAsync().WaitAsync(token);
        }, DispatcherPriority.Normal);
    }

    public async Task<Bitmap?> GetBitmapAsync(CancellationToken token)
    {
        return await Dispatcher.UIThread.InvokeAsync<Bitmap?>(async () =>
        {
            return await _clipboard.TryGetBitmapAsync().WaitAsync(token);
        }, DispatcherPriority.Normal);
    }

    public async Task<IStorageItem[]?> GetFilesAsync(CancellationToken token)
    {
        return await Dispatcher.UIThread.InvokeAsync<IStorageItem[]?>(async () =>
        {
            return await _clipboard.TryGetFilesAsync().WaitAsync(token);
        }, DispatcherPriority.Normal);
    }

    public async Task<object?> GetDataAsync(string format, CancellationToken token)
    {
        return await Dispatcher.UIThread.InvokeAsync<object?>(async () =>
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

            using var dataTransfer = await _clipboard.TryGetDataAsync().WaitAsync(token);
            if (dataTransfer is null)
                return null;

            // 尝试使用平台格式获取数据
            var platformFormat = DataFormat.CreateBytesPlatformFormat(format);
            var bytes = await dataTransfer.TryGetValueAsync(platformFormat).WaitAsync(token);
            if (bytes is not null)
                return bytes;

            // 尝试使用字符串平台格式
            var stringFormat = DataFormat.CreateStringPlatformFormat(format);
            var str = await dataTransfer.TryGetValueAsync(stringFormat).WaitAsync(token);
            return str;
        }, DispatcherPriority.Normal);
    }

    [SupportedOSPlatform("linux")]
    public async Task<int?> GetTimeStamp(CancellationToken token)
    {
        return await Dispatcher.UIThread.InvokeAsync<int?>(async () =>
        {
            using var dataTransfer = await _clipboard.TryGetDataAsync().WaitAsync(token);
            if (dataTransfer is null)
                return null;

            var platformFormat = DataFormat.CreateBytesPlatformFormat(Format.TimeStamp);
            var bytes = await dataTransfer.TryGetValueAsync(platformFormat).WaitAsync(token);
            if (bytes is null)
                return null;

            var str = Encoding.UTF8.GetString(bytes);
            bool canParse = int.TryParse(str, out var result);
            return canParse ? result : BitConverter.ToInt32(bytes);
        }, DispatcherPriority.Normal);
    }
}